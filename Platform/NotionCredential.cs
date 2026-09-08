using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace TerminalWidget.Platform;
public static class NotionCredential
{
    private const string Target="TerminalWidget.Notion.v1";
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Credential { public uint Flags,Type;public string TargetName;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint Size;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? Alias,User; }
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool Read(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    public static void Save(string token)
    {
        byte[] bytes=Encoding.Unicode.GetBytes(token);if(bytes.Length>2500)throw new ArgumentException("토큰이 너무 깁니다.");
        var p=Marshal.AllocCoTaskMem(bytes.Length);
        try {Marshal.Copy(bytes,0,p,bytes.Length);var c=new Credential {Type=1,TargetName=Target,Size=(uint)bytes.Length,Blob=p,Persist=2,User="Notion"};if(!Write(ref c,0))throw new Win32Exception(Marshal.GetLastWin32Error());}
        finally {for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(p,i,0);Marshal.FreeCoTaskMem(p);Array.Clear(bytes);}
    }
    public static string? Load()
    {
        if(!Read(Target,1,0,out var p)){if(Marshal.GetLastWin32Error()==1168)return null;throw new Win32Exception(Marshal.GetLastWin32Error());}
        try {var c=Marshal.PtrToStructure<Credential>(p);return Marshal.PtrToStringUni(c.Blob,(int)c.Size/2);}finally{CredFree(p);}
    }
}
