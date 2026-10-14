using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;

namespace FpkgExfat
{
    internal static class PkgFileSaveJob
    {
        internal static string CreateUpdate(PkgFileSession session,string output,string temporary,string version,string publisher,string passcode,
            CancellationToken token,Action<ConversionProgress> report,Action<string> log)
        {
            version=(version??"").Trim();
            if(!BackportVersion.IsValid(version)||!BackportVersion.IsValid(session.Snapshot.ContentVersion)||BackportVersion.Compare(version,session.Snapshot.ContentVersion)<=0)
                throw new ArgumentException("Per un aggiornamento inserisci una versione maggiore di "+session.Snapshot.ContentVersion+", per esempio "+BackportVersion.Next(session.Snapshot.ContentVersion)+".");
            if(!File.Exists(publisher)||!Path.GetFileName(publisher).Equals("prospero-pub-cmd.exe",StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("Seleziona prospero-pub-cmd.exe dal tuo Sony SDK.",publisher);
            output=Path.GetFullPath(output);temporary=Path.GetFullPath(temporary);
            if(File.Exists(output)||Directory.Exists(output)||output.Equals(session.Snapshot.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Scegli un nuovo nome per il PKG aggiornamento.");
            Directory.CreateDirectory(temporary);
            long needed=0;foreach(PkgFileItem item in session.Files)if(!item.Added)needed=checked(needed+item.Size);
            if(FileWorkspace.FreeBytes(temporary)<needed)throw new IOException("Serve almeno "+FileWorkspace.Size(needed)+" per ricavare la cartella gioco dal PKG, oltre ai temporanei dell’SDK.");
            string work=FileWorkspace.CreateOwned(temporary);
            try
            {
                string game=Path.Combine(work,"game"),overlay=Path.Combine(work,"overlay");Directory.CreateDirectory(overlay);
                log("Update dal solo PKG: prima ricavo il contenuto completo del base. L’SDK può rileggerlo e richiedere altro spazio temporaneo.");
                session.Materialize(game,false,token,report,log);
                foreach(PkgFileItem item in session.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if(item.Removed)
                    { string path=Path.Combine(game,item.Path.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(path))File.Delete(path); }
                    else if(item.Replacement!=null)session.Export(item,Path.Combine(overlay,item.Path.Replace('/',Path.DirectorySeparatorChar)),false,token);
                }
                string param=Path.Combine(overlay,"sce_sys","param.json");
                if(!File.Exists(param)){Directory.CreateDirectory(Path.GetDirectoryName(param));File.WriteAllBytes(param,session.Snapshot.ParamBytes);}
                return BackportUpdateJob.Run(new BackportUpdateRequest{BasePackage=session.Snapshot.Path,GameFolder=game,BackportFolder=overlay,
                    Files=BackportFileSet.Load(overlay),PublisherTool=publisher,Passcode=passcode,ContentVersion=version,OutputPackage=output,WorkParent=work,CompressionLevel=7},token,report,log);
            }
            finally { FileWorkspace.DeleteOwned(work,log); }
        }

        internal static string Rebuild(PkgFileSession session,string output,string temporary,string version,CancellationToken token,
            Func<string,string,string,CancellationToken,object> prepare,Action<ConversionProgress> report,Action<string> log)
        {
            version=(version??"").Trim();
            if(!BackportVersion.IsValid(version))throw new ArgumentException("Inserisci una versione come 01.001.004.");
            output=Path.GetFullPath(output);temporary=Path.GetFullPath(temporary);
            if(File.Exists(output)||Directory.Exists(output)||output.Equals(session.Snapshot.Path,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Scegli un nuovo file PKG: il base e i file esistenti vengono conservati.");
            AtomicFileExport.RequireOrdinaryPath(temporary);
            long needed=0;foreach(PkgFileItem item in session.Files)if(!item.Removed)needed=checked(needed+item.CurrentSize);
            Directory.CreateDirectory(temporary);
            if(FileWorkspace.FreeBytes(temporary)<needed)throw new IOException("La ricostruzione richiede almeno "+FileWorkspace.Size(needed)+" per la cartella temporanea, oltre allo spazio del nuovo PKG.");
            string work=null,outputWork=null;
            try
            {
                work=FileWorkspace.CreateOwned(temporary);outputWork=FileWorkspace.CreateOwned(Path.GetDirectoryName(output));
                string dump=Path.Combine(work,"game"),scratch=Path.Combine(work,"builder-temp");
                Directory.CreateDirectory(scratch);
                log("Salvataggio PKG completo: il contenuto viene letto dal base e ricostruito. Non serve la cartella originale del gioco.");
                session.Materialize(dump,true,token,report,log);
                token.ThrowIfCancellationRequested();
                string paramPath=Path.Combine(dump,"sce_sys","param.json");
                PackageParam param=new PackageParam(File.ReadAllBytes(paramPath));
                if(!string.Equals(param.Text("contentId"),session.Snapshot.ContentId,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Il param.json importato appartiene a un Content ID diverso.");
                param.SetText("contentVersion",version);File.WriteAllBytes(paramPath,param.Bytes());
                report(new ConversionProgress("Ricostruisco il PKG completo","Creo e verifico un nuovo pacchetto",0,0));
                object options=prepare(dump,outputWork,scratch,token);
                BuilderBridge.Set(options,"Version",version);
                BuilderBridge.Set(options,"Passcode",session.Passcode);
                string built=Path.GetFullPath(BuilderBridge.Build(options,log));
                if(!File.Exists(built)||!FileWorkspace.Inside(built,outputWork))throw new InvalidDataException("Il builder non ha prodotto il PKG nella cartella prevista.");
                token.ThrowIfCancellationRequested();
                report(new ConversionProgress("Verifico il nuovo PKG","Controllo il pacchetto prima di salvarlo",0,0));
                BuilderBridge.Verify(built,options,token,log);
                token.ThrowIfCancellationRequested();FileWorkspace.Commit(built,output);
                log("Nuovo PKG salvato: "+output);return output;
            }
            finally { FileWorkspace.DeleteOwned(work,log);FileWorkspace.DeleteOwned(outputWork,log); }
        }

        internal static void SaveEdits(PkgFileSession session,string folder,string version,CancellationToken token,Action<string> log)
        {
            version=(version??"").Trim();
            if(session.Changes==0)throw new InvalidDataException("Non ci sono file importati o rimossi da salvare.");
            if(!BackportVersion.IsValid(version))throw new ArgumentException("Inserisci una versione come 01.001.004.");
            folder=Path.GetFullPath(folder);
            if(File.Exists(folder)||Directory.Exists(folder))throw new IOException("Scegli una nuova cartella per le modifiche.");
            string owner=FileWorkspace.CreateOwned(Path.GetDirectoryName(folder)),staged=Path.Combine(owner,"edits");
            try
            {
                Directory.CreateDirectory(Path.Combine(staged,"overlay"));
                using(XmlWriter writer=XmlWriter.Create(Path.Combine(staged,"pkg-edits.xml"),new XmlWriterSettings{Indent=true}))
                {
                    writer.WriteStartElement("pkg-edits");writer.WriteAttributeString("format","1");
                    writer.WriteAttributeString("content-id",session.Snapshot.ContentId);writer.WriteAttributeString("version",version);
                    writer.WriteAttributeString("base-header-sha256",BitConverter.ToString(session.Snapshot.IdentityHash).Replace("-",""));
                    foreach(PkgFileItem file in session.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        if(file.Replacement==null&&!file.Removed)continue;
                        writer.WriteStartElement("file");writer.WriteAttributeString("path",file.Path);
                        writer.WriteAttributeString("action",file.Removed?"remove":"import");writer.WriteEndElement();
                        if(!file.Removed)session.Export(file,Path.Combine(staged,"overlay",file.Path.Replace('/',Path.DirectorySeparatorChar)),false,token);
                    }
                    writer.WriteEndElement();
                }
                token.ThrowIfCancellationRequested();Directory.Move(staged,folder);
                if(log!=null)log("Modifiche salvate in "+folder+". Questo progetto contiene solo i file modificati; non è un PKG installabile.");
            }
            finally { FileWorkspace.DeleteOwned(owner,log); }
        }

        internal static string LoadEdits(PkgFileSession session,string manifest)
        {
            FileWorkspace.NoReparse(manifest);
            XmlDocument document=new XmlDocument{XmlResolver=null};
            using(XmlReader reader=XmlReader.Create(manifest,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024}))document.Load(reader);
            XmlElement root=document.DocumentElement;
            if(root==null||root.Name!="pkg-edits"||root.GetAttribute("format")!="1"||
                root.GetAttribute("content-id")!=session.Snapshot.ContentId||root.GetAttribute("base-header-sha256")!=BitConverter.ToString(session.Snapshot.IdentityHash).Replace("-",""))
                throw new InvalidDataException("Il progetto non corrisponde al PKG aperto.");
            string version=root.GetAttribute("version");if(!BackportVersion.IsValid(version))throw new InvalidDataException("Versione del progetto non valida.");
            List<KeyValuePair<string,string>> imports=new List<KeyValuePair<string,string>>();List<PkgFileItem> removed=new List<PkgFileItem>();
            HashSet<string> paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(XmlNode node in root.ChildNodes)
            {
                XmlElement element=node as XmlElement;if(element==null||element.Name!="file")continue;
                string path=BackportPath.NormalizeRelative(element.GetAttribute("path"));
                if(!paths.Add(path))throw new InvalidDataException("Percorso duplicato nel progetto.");
                if(element.GetAttribute("action")=="remove")
                {
                    PkgFileItem item=session.Find(path);if(item==null||path.Equals("eboot.bin",StringComparison.OrdinalIgnoreCase)||path.Equals("sce_sys/param.json",StringComparison.OrdinalIgnoreCase)||path.Equals("sce_sys/keystone",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Rimozione non valida: "+path);
                    removed.Add(item);
                }
                else if(element.GetAttribute("action")=="import")
                {
                    string source=Path.Combine(Path.GetDirectoryName(manifest),"overlay",path.Replace('/',Path.DirectorySeparatorChar));
                    FileWorkspace.NoReparse(source);if(!File.Exists(source)||BackportPath.SkipReason(path)!=null)throw new InvalidDataException("File importato mancante o riservato: "+path);
                    imports.Add(new KeyValuePair<string,string>(path,source));
                }
                else throw new InvalidDataException("Azione del progetto non valida.");
            }
            foreach(KeyValuePair<string,string> item in imports)session.Import(item.Key,item.Value);
            foreach(PkgFileItem item in removed)session.Remove(item);
            return version;
        }
    }
}
