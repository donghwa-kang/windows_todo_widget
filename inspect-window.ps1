$source = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WidgetInspect {
 public delegate bool Callback(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback c, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, Callback c, IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int n);
 public struct Rect {public int Left,Top,Right,Bottom;}
 public static string Describe(IntPtr h) {uint p; GetWindowThreadProcessId(h,out p); var s=new StringBuilder(256); GetClassName(h,s,256);Rect r;GetWindowRect(h,out r);return String.Format("handle={0} pid={1} parent={2} class={3} visible={4} rect={5},{6},{7},{8} style={9:X} ex={10:X}",h,p,GetParent(h),s,IsWindowVisible(h),r.Left,r.Top,r.Right,r.Bottom,GetWindowLongPtr(h,-16).ToInt64(),GetWindowLongPtr(h,-20).ToInt64());}
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
 public static string Inspect(uint target) {var output=new StringBuilder();Callback child=(h,p)=>{uint id;GetWindowThreadProcessId(h,out id);if(id==target && IsWindowVisible(h)){output.AppendLine(Describe(h));var parent=GetParent(h);for(int i=0;parent!=IntPtr.Zero&&i<4;i++){output.AppendLine("ancestor "+Describe(parent));for(var sibling=GetWindow(parent,5);sibling!=IntPtr.Zero;sibling=GetWindow(sibling,2)) output.AppendLine("  z-order "+Describe(sibling));parent=GetParent(parent);}}return true;};EnumWindows((h,p)=>{child(h,p);EnumChildWindows(h,child,IntPtr.Zero);return true;},IntPtr.Zero);return output.ToString();}
}
'@
Add-Type -TypeDefinition $source
Get-Process -Name TerminalWidget -ErrorAction Stop | ForEach-Object { [WidgetInspect]::Inspect([uint32]$_.Id) }
