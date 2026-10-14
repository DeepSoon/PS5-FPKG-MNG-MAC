using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal sealed class PkgFileEditorControl : UserControl
    {
        private readonly Form owner;
        private readonly Func<bool> otherBusy;
        private readonly Func<string,string,string,CancellationToken,object> prepare;
        private readonly TextBox package=new TextBox{Dock=DockStyle.Fill,ReadOnly=true};
        private readonly TextBox passcode=new TextBox{Dock=DockStyle.Fill,Text=new string('0',32),UseSystemPasswordChar=true};
        private readonly TextBox filter=new TextBox{Width=200};
        private readonly TextBox version=new TextBox{Width=105,Text="01.001.005",MaxLength=11};
        private readonly TextBox temporary=new TextBox{Dock=DockStyle.Fill,Text=Path.Combine(Path.GetTempPath(),"PS5-FPKG-Builder")};
        private readonly Button open=new Button{Text="Apri PKG...",AutoSize=true,Tag="primary"};
        private readonly Button close=new Button{Text="Chiudi PKG",AutoSize=true};
        private readonly Button extract=new Button{Text="Extract to...",AutoSize=true};
        private readonly Button extractOriginal=new Button{Text="Estrai originale",AutoSize=true};
        private readonly Button replace=new Button{Text="Importa sostituzione...",AutoSize=true};
        private readonly Button add=new Button{Text="Aggiungi file...",AutoSize=true};
        private readonly Button addFolder=new Button{Text="Importa cartella...",AutoSize=true};
        private readonly Button remove=new Button{Text="Rimuovi dal nuovo PKG",AutoSize=true};
        private readonly Button restore=new Button{Text="Annulla modifica",AutoSize=true};
        private readonly Button saveEdits=new Button{Text="Salva solo modifiche...",AutoSize=true};
        private readonly Button loadEdits=new Button{Text="Apri modifiche...",AutoSize=true};
        private readonly Button rebuild=new Button{Text="Salva nuovo PKG (ricostruzione)...",AutoSize=true,Tag="primary"};
        private readonly Button updatePkg=new Button{Text="Crea update con SDK...",AutoSize=true};
        private readonly Button cancel=new Button{Text="Annulla operazione",AutoSize=true};
        private readonly Button tempBrowse=new Button{Text="Scegli...",AutoSize=true};
        private readonly DataGridView grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,VirtualMode=true,MultiSelect=true,
            SelectionMode=DataGridViewSelectionMode.FullRowSelect,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,
            AutoGenerateColumns=false,AllowUserToResizeRows=false,BorderStyle=BorderStyle.None};
        private readonly Label info=new Label{Dock=DockStyle.Fill,Text="Apri un PKG per leggere l’elenco dei file. Non serve la cartella del gioco.",AutoEllipsis=true,Tag="muted"};
        private readonly Label status=new Label{Dock=DockStyle.Fill,Text="Pronto",AutoEllipsis=true,Tag="muted"};
        private readonly Label count=new Label{AutoSize=true,Text="0 file",Margin=new Padding(12,8,0,0)};
        private readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill,Minimum=0,Maximum=1000};
        private readonly RichTextBox activity=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,WordWrap=false,DetectUrls=false,Font=new Font("Consolas",8.5f)};
        private readonly ConcurrentQueue<string> messages=new ConcurrentQueue<string>();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=200};
        private readonly List<PkgFileItem> visible=new List<PkgFileItem>();
        private PkgFileSession session;
        private CancellationTokenSource cancellation;
        private ConversionProgress latest;
        internal bool Busy { get;private set; }

        internal PkgFileEditorControl(Form parent,Func<bool> blocked,Func<string,string,string,CancellationToken,object> options)
        {
            owner=parent;otherBusy=blocked;prepare=options;Dock=DockStyle.Fill;Padding=new Padding(18,8,18,14);
            TableLayoutPanel root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,92));root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,156));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,29));root.RowStyles.Add(new RowStyle(SizeType.Absolute,9));root.RowStyles.Add(new RowStyle(SizeType.Absolute,100));
            TableLayoutPanel source=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=3};
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,275));
            source.RowStyles.Add(new RowStyle(SizeType.Absolute,33));source.RowStyles.Add(new RowStyle(SizeType.Absolute,30));source.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            source.Controls.Add(Label("PKG"),0,0);source.Controls.Add(package,1,0);source.SetColumnSpan(package,2);
            FlowLayoutPanel sourceActions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};sourceActions.Controls.AddRange(new Control[]{open,close});source.Controls.Add(sourceActions,3,0);
            source.Controls.Add(Label("Passcode"),0,1);source.Controls.Add(passcode,1,1);source.SetColumnSpan(passcode,3);
            source.Controls.Add(info,0,2);source.SetColumnSpan(info,4);root.Controls.Add(source,0,0);
            FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=true};
            actions.Controls.AddRange(new Control[]{extract,extractOriginal,replace,add,addFolder,remove,restore,Label("Cerca:"),filter,count});root.Controls.Add(actions,0,1);
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Path",HeaderText="Percorso nel PKG",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=70});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Size",HeaderText="Dimensione",Width=110});
            grid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="Stato",Width=180});root.Controls.Add(grid,0,2);
            TableLayoutPanel saving=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=4};
            saving.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));saving.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));saving.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
            saving.RowStyles.Add(new RowStyle(SizeType.Absolute,35));saving.RowStyles.Add(new RowStyle(SizeType.Absolute,35));saving.RowStyles.Add(new RowStyle(SizeType.Absolute,33));saving.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            FlowLayoutPanel saveActions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
            saveActions.Controls.AddRange(new Control[]{Label("Versione:"),version,saveEdits,loadEdits,cancel});
            saving.Controls.Add(saveActions,0,0);saving.SetColumnSpan(saveActions,3);
            FlowLayoutPanel packageActions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
            packageActions.Controls.AddRange(new Control[]{rebuild,updatePkg});saving.Controls.Add(packageActions,0,1);saving.SetColumnSpan(packageActions,3);
            saving.Controls.Add(Label("Cartella temporanea"),0,2);saving.Controls.Add(temporary,1,2);saving.Controls.Add(tempBrowse,2,2);
            Label note=new Label{Dock=DockStyle.Fill,Tag="muted",Text="Salva solo modifiche = piccolo progetto, non installabile. Nuovo PKG / update con SDK = ricavano il gioco dal base e leggono il contenuto completo; servono spazio temporaneo e tempo.",Font=new Font("Segoe UI",8.75f)};
            saving.Controls.Add(note,0,3);saving.SetColumnSpan(note,3);root.Controls.Add(saving,0,3);
            root.Controls.Add(status,0,4);root.Controls.Add(progress,0,5);root.Controls.Add(activity,0,6);Controls.Add(root);
            grid.CellValueNeeded+=delegate(object sender,DataGridViewCellValueEventArgs e)
            {
                if(e.RowIndex<0||e.RowIndex>=visible.Count)return;PkgFileItem item=visible[e.RowIndex];
                e.Value=e.ColumnIndex==0?(object)item.Path:e.ColumnIndex==1?(object)FileWorkspace.Size(item.CurrentSize):item.State;
            };
            grid.SelectionChanged+=delegate{RefreshControls();};filter.TextChanged+=delegate{RefreshFiles();};
            open.Click+=async delegate{await OpenPackage();};extract.Click+=async delegate{await Extract(false);};extractOriginal.Click+=async delegate{await Extract(true);};
            close.Click+=delegate
            {
                if(!Available||session==null)return;
                if(session.Changes>0&&MessageBox.Show(owner,"Chiudere il PKG e le modifiche in attesa? Salva prima il progetto se vuoi riprenderle.","Chiudi PKG",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                session.Dispose();session=null;package.Clear();info.Text="Apri un PKG per leggere l’elenco dei file.";Report(new ConversionProgress("Pronto","PKG chiuso",0,0));RefreshFiles();
            };
            replace.Click+=delegate{ImportReplacement();};add.Click+=delegate{AddFiles();};addFolder.Click+=delegate{ImportFolder();};
            remove.Click+=delegate{EditSelected(true);};restore.Click+=delegate{EditSelected(false);};
            saveEdits.Click+=async delegate{await SaveEdits();};loadEdits.Click+=delegate{LoadEdits();};rebuild.Click+=async delegate{await Rebuild();};updatePkg.Click+=async delegate{await CreateUpdate();};
            cancel.Click+=delegate{if(cancellation!=null){cancellation.Cancel();status.Text="Annullamento in corso...";}};
            tempBrowse.Click+=delegate{using(FolderBrowserDialog dialog=new FolderBrowserDialog{Description="Cartella temporanea per la ricostruzione completa"})if(dialog.ShowDialog(owner)==DialogResult.OK)temporary.Text=dialog.SelectedPath;};
            ContextMenuStrip menu=new ContextMenuStrip();ToolStripMenuItem menuExtract=new ToolStripMenuItem("Extract to...");
            menuExtract.Click+=async delegate{await Extract(false);};menu.Items.Add(menuExtract);
            menu.Items.Add("Importa sostituzione...",null,delegate{ImportReplacement();});menu.Items.Add("Rimuovi dal nuovo PKG",null,delegate{EditSelected(true);});menu.Items.Add("Annulla modifica",null,delegate{EditSelected(false);});
            menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e){e.Cancel=Busy||otherBusy()||Selected().Count==0;};grid.ContextMenuStrip=menu;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Right&&e.RowIndex>=0&&!grid.Rows[e.RowIndex].Selected){grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;grid.CurrentCell=grid.Rows[e.RowIndex].Cells[0];}};
            timer.Tick+=delegate{string message;while(messages.TryDequeue(out message))activity.AppendText(message+Environment.NewLine);ConversionProgress update=Interlocked.CompareExchange(ref latest,null,null);if(update!=null){status.Text=update.Stage+(string.IsNullOrEmpty(update.Detail)?"":" · "+update.Detail);progress.Style=Busy&&update.Total==0?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;progress.Value=update.Total>0?(int)Math.Min(1000,Math.Max(0,update.Done*1000.0/update.Total)):0;}RefreshControls();};
            timer.Start();RefreshControls();
        }
        private static Label Label(string text) { return new Label{Text=text,AutoSize=true,Margin=new Padding(4,7,4,0)}; }
        private bool Available { get { return !Busy&&!otherBusy(); } }
        private void Log(string text) { messages.Enqueue("["+DateTime.Now.ToString("HH:mm:ss")+"] "+text); }
        private void Report(ConversionProgress update) { Interlocked.Exchange(ref latest,update); }
        private void RefreshControls()
        {
            bool ready=Available,loaded=session!=null,selected=grid.SelectedRows.Count>0;
            open.Enabled=passcode.Enabled=ready;close.Enabled=ready&&loaded;filter.Enabled=ready&&loaded;
            extract.Enabled=extractOriginal.Enabled=remove.Enabled=restore.Enabled=ready&&loaded&&selected;
            replace.Enabled=ready&&loaded&&grid.SelectedRows.Count==1;add.Enabled=addFolder.Enabled=loadEdits.Enabled=ready&&loaded;
            saveEdits.Enabled=ready&&loaded&&session.Changes>0;rebuild.Enabled=ready&&loaded&&BackportVersion.IsValid(version.Text);
            updatePkg.Enabled=ready&&loaded&&session.Changes>0&&BackportVersion.IsValid(version.Text)&&BackportVersion.IsValid(session.Snapshot.ContentVersion)&&BackportVersion.Compare(version.Text,session.Snapshot.ContentVersion)>0;
            version.Enabled=temporary.Enabled=tempBrowse.Enabled=ready;cancel.Enabled=Busy;grid.Enabled=!Busy;
        }
        private void RefreshFiles()
        {
            visible.Clear();string text=filter.Text.Trim();
            if(session!=null)foreach(PkgFileItem item in session.Files)if(text.Length==0||item.Path.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0)visible.Add(item);
            visible.Sort(delegate(PkgFileItem a,PkgFileItem b){return StringComparer.OrdinalIgnoreCase.Compare(a.Path,b.Path);});
            grid.RowCount=0;grid.RowCount=visible.Count;grid.Invalidate();count.Text=visible.Count+" file"+(session==null?"":" · "+session.Changes+" modifiche");RefreshControls();
        }
        private List<PkgFileItem> Selected()
        { List<PkgFileItem> files=new List<PkgFileItem>();foreach(DataGridViewRow row in grid.SelectedRows)if(row.Index>=0&&row.Index<visible.Count)files.Add(visible[row.Index]);return files; }
        private async Task Operation(string title,Func<CancellationToken,Task> work)
        {
            if(!Available)return;Busy=true;cancellation=new CancellationTokenSource();Report(new ConversionProgress(title,"",0,0));RefreshControls();
            try{await work(cancellation.Token);Report(new ConversionProgress("Completato",title,1,1));}
            catch(OperationCanceledException){Log("Operazione annullata.");Report(new ConversionProgress("Annullato","",0,0));}
            catch(Exception error){Log(error.ToString());Report(new ConversionProgress("Errore",error.Message,0,0));MessageBox.Show(owner,error.Message,"Extract file from PKG",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{Busy=false;cancellation.Dispose();cancellation=null;RefreshFiles();}
        }
        private async Task OpenPackage()
        {
            if(!Available)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Filter="PS5 PKG (*.pkg)|*.pkg"})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                if(session!=null&&session.Changes>0&&MessageBox.Show(owner,"Le modifiche in attesa verranno chiuse. Hai già salvato il progetto o vuoi continuare comunque?","Apri altro PKG",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                string path=dialog.FileName,code=passcode.Text;
                await Operation("Leggo l’indice del PKG",async delegate(CancellationToken token)
                {
                    PkgFileSession next=await Task.Run(delegate{return PkgFileSession.Open(path,code,token,Log);});
                    if(session!=null)session.Dispose();session=next;package.Text=path;
                    version.Text=BackportVersion.IsValid(session.Snapshot.ContentVersion)?session.Snapshot.ContentVersion:"01.001.004";
                    info.Text=session.Snapshot.ContentId+" · versione "+session.Snapshot.ContentVersion+" · "+FileWorkspace.Size(session.Snapshot.Length)+" · "+(session.Snapshot.Plaintext?"lettura diretta":"decodifica dei soli blocchi richiesti");
                });
            }
        }
        private async Task Extract(bool original)
        {
            if(!Available||session==null)return;List<PkgFileItem> chosen=Selected();if(chosen.Count==0)return;
            using(FolderBrowserDialog dialog=new FolderBrowserDialog{Description="Extract to: scegli la cartella. I percorsi interni vengono mantenuti."})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;string folder=dialog.SelectedPath;
                await Operation("Estrazione dei file selezionati",async delegate(CancellationToken token)
                {
                    await Task.Run(delegate
                    {
                        int done=0;
                        foreach(PkgFileItem file in chosen)
                        { session.Export(file,Path.Combine(folder,file.Path.Replace('/',Path.DirectorySeparatorChar)),original,token);Log("Estratto: "+file.Path);Report(new ConversionProgress("Estraggo solo la selezione",file.Path,++done,chosen.Count)); }
                    });
                    Log("Estrazione completata: "+folder);
                });
            }
        }
        private void ImportReplacement()
        {
            if(!Available||session==null)return;List<PkgFileItem> files=Selected();if(files.Count!=1)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Title="Importa il nuovo file al posto di "+files[0].Path,Filter="Tutti i file (*.*)|*.*"})
                if(dialog.ShowDialog(owner)==DialogResult.OK)TryEdit(delegate{session.Import(files[0].Path,dialog.FileName);Log("Sostituzione in attesa di salvataggio: "+files[0].Path);});
        }
        private void AddFiles()
        {
            if(!Available||session==null)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Title="Aggiungi file al nuovo PKG",Multiselect=false,Filter="Tutti i file (*.*)|*.*"})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                string path=PromptPath("Percorso nel PKG (es. fakelib/libSceAgc.sprx)",Path.GetFileName(dialog.FileName));
                if(path!=null)TryEdit(delegate{session.Import(path,dialog.FileName);Log("File importato: "+path);});
            }
        }
        private string PromptPath(string caption,string value)
        {
            using(Form prompt=new Form{Text="Percorso nel PKG",Width=590,Height=160,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
            {
                TextBox field=new TextBox{Left=14,Top=40,Width=545,Text=value};Button ok=new Button{Text="Importa",Left=395,Top=76,Width=80,DialogResult=DialogResult.OK};Button no=new Button{Text="Annulla",Left=480,Top=76,Width=80,DialogResult=DialogResult.Cancel};
                prompt.Controls.AddRange(new Control[]{new Label{Left=14,Top=12,Width=545,Text=caption},field,ok,no});prompt.AcceptButton=ok;prompt.CancelButton=no;
                return prompt.ShowDialog(owner)==DialogResult.OK?field.Text:null;
            }
        }
        private void ImportFolder()
        {
            if(!Available||session==null)return;
            using(FolderBrowserDialog dialog=new FolderBrowserDialog{Description="Cartella con eboot.bin, fakelib, sce_module ecc. I percorsi vengono mantenuti."})
                if(dialog.ShowDialog(owner)==DialogResult.OK)TryEdit(delegate
                {
                    BackportFileSet imported=BackportFileSet.Load(dialog.SelectedPath);int n=0;
                    foreach(BackportFileItem file in imported.Items)if(string.IsNullOrEmpty(file.SkipReason)){session.Import(file.RelativePath,file.OriginalSourcePath);n++;}
                    Log("Importati "+n+" file dalla cartella; il PKG base non è stato riletto.");
                });
        }
        private void EditSelected(bool delete)
        { if(!Available||session==null)return;List<PkgFileItem> files=Selected();TryEdit(delegate{foreach(PkgFileItem file in files)if(delete)session.Remove(file);else session.Restore(file);}); }
        private void TryEdit(Action action)
        { try{action();}catch(Exception error){MessageBox.Show(owner,error.Message,"Modifica PKG",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{RefreshFiles();} }
        private async Task SaveEdits()
        {
            if(!Available||session==null)return;
            using(SaveFileDialog dialog=new SaveFileDialog{Title="Scegli il nome di una nuova cartella per il progetto",FileName=Path.GetFileNameWithoutExtension(package.Text)+"-modifiche",Filter="Cartella progetto|*.*",AddExtension=false,OverwritePrompt=false})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;string folder=dialog.FileName,v=version.Text;
                await Operation("Salvo soltanto i file modificati",async delegate(CancellationToken token){await Task.Run(delegate{PkgFileSaveJob.SaveEdits(session,folder,v,token,Log);});});
            }
        }
        private void LoadEdits()
        {
            if(!Available||session==null)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Title="Apri un progetto di modifiche",FileName="pkg-edits.xml",Filter="Progetto PKG (pkg-edits.xml)|pkg-edits.xml"})
                if(dialog.ShowDialog(owner)==DialogResult.OK)TryEdit(delegate{version.Text=PkgFileSaveJob.LoadEdits(session,dialog.FileName);Log("Modifiche caricate dal progetto.");});
        }
        private async Task Rebuild()
        {
            if(!Available||session==null)return;
            using(SaveFileDialog dialog=new SaveFileDialog{Title="Salva nuovo PKG: ricostruzione completa",FileName=Path.GetFileNameWithoutExtension(package.Text)+"-edited.pkg",Filter="PS5 PKG (*.pkg)|*.pkg",OverwritePrompt=false})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;string target=dialog.FileName,temp=temporary.Text,v=version.Text;
                await Operation("Salvataggio del nuovo PKG",async delegate(CancellationToken token)
                {
                    string result=await Task.Run(delegate{return PkgFileSaveJob.Rebuild(session,target,temp,v,token,prepare,Report,Log);});
                    MessageBox.Show(owner,"Nuovo PKG creato e verificato:\r\n"+result,"PKG salvato",MessageBoxButtons.OK,MessageBoxIcon.Information);
                });
            }
        }
        private async Task CreateUpdate()
        {
            if(!Available||session==null)return;
            using(OpenFileDialog tool=new OpenFileDialog{Title="Seleziona il tuo prospero-pub-cmd.exe",Filter="Sony publisher|prospero-pub-cmd.exe"})
            {
                if(tool.ShowDialog(owner)!=DialogResult.OK)return;string publisher=tool.FileName;
                using(SaveFileDialog dialog=new SaveFileDialog{Title="PKG aggiornamento: legge il contenuto completo dal base",FileName=Path.GetFileNameWithoutExtension(package.Text)+"-update.pkg",Filter="PS5 PKG (*.pkg)|*.pkg",OverwritePrompt=false})
                {
                    if(dialog.ShowDialog(owner)!=DialogResult.OK)return;string target=dialog.FileName,temp=temporary.Text,v=version.Text,code=session.Passcode;
                    await Operation("Creazione aggiornamento dal solo PKG",async delegate(CancellationToken token)
                    {
                        string result=await Task.Run(delegate{return PkgFileSaveJob.CreateUpdate(session,target,temp,v,publisher,code,token,Report,Log);});
                        MessageBox.Show(owner,"PKG aggiornamento creato:\r\n"+result,"PKG salvato",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    });
                }
            }
        }
        protected override void Dispose(bool disposing)
        { if(disposing){timer.Stop();timer.Dispose();if(session!=null)session.Dispose();}base.Dispose(disposing); }
    }
}
