using System;
using System.IO;
using System.Threading;
using System.Xml;
using FpkgExfat;

internal static class PackageEditProjectTests
{
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static PkgFileSession Session(string root)
    {
        PkgFileSession session=new PkgFileSession();
        session.Snapshot=new PackageSnapshot{Path=Path.Combine(root,"base.pkg"),ContentId="IV9999-FAKE00001_00-HOMEBREW00000001",ContentVersion="01.001.004",IdentityHash=new byte[32]};
        session.Files.Add(new PkgFileItem{Path="eboot.bin",Node=new object(),Size=3});
        session.Files.Add(new PkgFileItem{Path="data/original.dat",Node=new object(),Size=5});
        return session;
    }
    public static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"pkg-edits-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string source=Path.Combine(root,"replacement.bin");File.WriteAllBytes(source,new byte[]{8,7,6,5});
            PkgFileSession session=Session(root);
            session.Import("data/new.bin",source);session.Remove(session.Find("data/new.bin"));
            Check(session.Changes==0&&session.Find("data/new.bin")==null,"Removing a newly added file must cancel the addition");
            session.Import("eboot.bin",source);session.Import("data/new.bin",source);session.Remove(session.Find("data/original.dat"));
            string project=Path.Combine(root,"saved-edits");
            PkgFileSaveJob.SaveEdits(session,project," 01.001.005 ",CancellationToken.None,null);
            Check(File.ReadAllBytes(Path.Combine(project,"overlay","eboot.bin"))[0]==8,"Project contains exact replacement bytes");
            Check(File.ReadAllBytes(Path.Combine(project,"overlay","data","new.bin")).Length==4,"Project preserves paths for additions");
            Check(!File.Exists(Path.Combine(project,"overlay","data","original.dat")),"Removed base files are not copied into the project");
            PkgFileSession reopened=Session(root);
            string version=PkgFileSaveJob.LoadEdits(reopened,Path.Combine(project,"pkg-edits.xml"));
            Check(version=="01.001.005","Version is normalized before saving");
            Check(reopened.Changes==3&&reopened.Find("data/original.dat").Removed&&reopened.Find("data/new.bin").Added,"Saved imports and removals reopen against the same base");
            bool overwrite=false;try{PkgFileSaveJob.SaveEdits(session,project,"01.001.005",CancellationToken.None,null);}catch(IOException){overwrite=true;}
            Check(overwrite&&File.ReadAllBytes(Path.Combine(project,"overlay","eboot.bin"))[0]==8,"Saving refuses to overwrite an existing project");
            PkgFileSession wrong=Session(root);wrong.Snapshot.IdentityHash[0]=1;
            bool mismatch=false;try{PkgFileSaveJob.LoadEdits(wrong,Path.Combine(project,"pkg-edits.xml"));}catch(InvalidDataException){mismatch=true;}
            Check(mismatch&&wrong.Changes==0,"A project for a different base is rejected before applying edits");
            bool protectedFile=false;try{session.Remove(session.Find("eboot.bin"));}catch(NotSupportedException){protectedFile=true;}
            Check(protectedFile&&!session.Find("eboot.bin").Removed,"eboot cannot be deleted accidentally");
            session.Restore(session.Find("eboot.bin"));Check(session.Find("eboot.bin").Replacement==null,"Restoring replacement preserves the original base entry");
            Console.WriteLine("PASS real edit-model operations and saved project round trip");return 0;
        }
        finally { Directory.Delete(root,true); }
    }
}
