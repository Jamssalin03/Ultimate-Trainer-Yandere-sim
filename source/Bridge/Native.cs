using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;

namespace YandereUltimate
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] struct SA { public int Length; public IntPtr Descriptor; public int Inherit; }
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int length, out int required);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr str);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafePipeHandle CreateNamedPipeW(string name, uint mode, uint pipeMode, uint instances, uint outSize, uint inSize, uint timeout, ref SA security);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFileW(string name,uint access,uint sharing,ref SA security,uint creation,uint flags,IntPtr template);
        static IntPtr Descriptor()
        {
            if(!OpenProcessToken(GetCurrentProcess(),8,out var token)) throw new Win32Exception();
            IntPtr buffer=IntPtr.Zero, sidText=IntPtr.Zero;
            try
            {
                GetTokenInformation(token,1,IntPtr.Zero,0,out int length);
                buffer=Marshal.AllocHGlobal(length);
                if(!GetTokenInformation(token,1,buffer,length,out length)) throw new Win32Exception();
                if(!ConvertSidToStringSidW(Marshal.ReadIntPtr(buffer),out sidText)) throw new Win32Exception();
                string sid=Marshal.PtrToStringUni(sidText);
                if(!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;"+sid+")",1,out var descriptor,out var size)) throw new Win32Exception();
                return descriptor;
            }
            finally { if(buffer!=IntPtr.Zero) Marshal.FreeHGlobal(buffer); if(sidText!=IntPtr.Zero) LocalFree(sidText); CloseHandle(token); }
        }
        public static void WritePrivate(string path,string content)
        {
            IntPtr descriptor=Descriptor();
            try
            {
                // Recreate our own ephemeral endpoint so a previous ACL is not reused.
                if(File.Exists(path)) File.Delete(path);
                var sa=new SA { Length=Marshal.SizeOf(typeof(SA)),Descriptor=descriptor };
                using(var handle=CreateFileW(path,0x40000000,1,ref sa,1,0x80,IntPtr.Zero))
                {
                    if(handle.IsInvalid) throw new Win32Exception();
                    using(var file=new FileStream(handle,FileAccess.Write))
                    using(var writer=new StreamWriter(file)) writer.Write(content);
                }
            }
            finally { LocalFree(descriptor); }
        }
        public static NamedPipeServerStream CreatePipe(string name)
        {
            IntPtr descriptor=Descriptor();
            try
            {
                var sa=new SA { Length=Marshal.SizeOf(typeof(SA)),Descriptor=descriptor };
                // Duplex, byte stream, first-instance protection, reject remote clients.
                var handle=CreateNamedPipeW(@"\\.\pipe\"+name,3|0x80000,8,1,65536,65536,0,ref sa);
                if(handle.IsInvalid) throw new Win32Exception();
                return new NamedPipeServerStream(PipeDirection.InOut,false,false,handle);
            }
            finally { LocalFree(descriptor); }
        }
    }
}
