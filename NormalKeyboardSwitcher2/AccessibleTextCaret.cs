using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

namespace NormalKeyboardSwitcher2;

[ComImport,Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleServices
{
    [PreserveSig] int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object result);
}
[ComImport,Guid("24fd2ffb-3aad-4a08-8335-a3ad89c0fb4b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleCaretText
{
    void AddSelection(); void Attributes(); // Unused vtable slots; never called.
    [PreserveSig] int CaretOffset(out int offset);
    [PreserveSig] int CharacterExtents(int offset,int coordinates,out int x,out int y,out int width,out int height);
}
internal static class AccessibleTextCaret
{
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromEvent(nint window,int objectId,int childId,[MarshalAs(UnmanagedType.Interface)] out IAccessible root,[MarshalAs(UnmanagedType.Struct)] out object child);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Native.Point point);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromPoint(Native.Point point,[MarshalAs(UnmanagedType.Interface)] out IAccessible root,[MarshalAs(UnmanagedType.Struct)] out object child);
    internal static PixelRect? Read(nint window,List<string> trace,AccessibleFocus? focusEvent=null,bool requireFocused=false,Native.Point? clickedPoint=null)
    {
        var objects=new List<object>();
        void Track(object? value) { if(value!=null && Marshal.IsComObject(value) && !objects.Exists(x=>ReferenceEquals(x,value))) objects.Add(value); }
        try
        {
            var iid=typeof(IAccessible).GUID;
            IAccessible? root;
            object? eventChild=null;
            if(focusEvent?.Kind==uint.MaxValue) { GetCursorPos(out var pointer); clickedPoint=pointer; }
            int resultCode;
            if(clickedPoint is { } point)
            {
                Native.GetWindowThreadProcessId(WindowFromPoint(point),out uint hovered);
                Native.GetWindowThreadProcessId(window,out uint expected);
                if(hovered!=expected) return null;
                resultCode=AccessibleObjectFromPoint(point,out root,out eventChild);
            }
            else resultCode=focusEvent is { } evt
                ? AccessibleObjectFromEvent(evt.Window,evt.ObjectId,evt.ChildId,out root,out eventChild)
                : Native.AccessibleObjectFromWindow(window,unchecked((uint)-4),ref iid,out root);
            if(resultCode<0 || root==null) return null;
            Track(root);
            if(eventChild is int childId && childId!=0 && root.get_accChild(childId) is IAccessible eventObject)
            { root=eventObject; Track(root); }

            PixelRect? ReadText(IAccessible current)
            {
                object? candidate=current;
                if(candidate is not IAccessibleCaretText && current is IAccessibleServices services)
                {
                    var service=typeof(IAccessible).GUID;
                    var textId=typeof(IAccessibleCaretText).GUID;
                    if(services.QueryService(ref service,ref textId,out var result)>=0) { candidate=result; Track(result); }
                }
                if(candidate is not IAccessibleCaretText text) return null;
                if(text.CaretOffset(out int offset)!=0 || offset<0) return null;
                // Geometry only: never retrieve text, names, values or selection contents.
                if(text.CharacterExtents(offset,0,out int x,out int y,out int width,out int height)!=0) return null;
                var bounds=new PixelRect(x,y,1,height);
                if(height<=1)
                {
                    current.accLocation(out int cx,out int cy,out int cw,out int ch,0);
                    bounds=CaretGeometry.ExpandPlaceholder(bounds,new(cx,cy,cw,ch));
                    if(bounds.Height>height) trace.Add("IA2 caret height from caret object");
                }
                return bounds.Height>0 && bounds.Height<500 && width>=0 && width<500 ? bounds : null;
            }

            if(clickedPoint!=null)
            {
                // Restrict fallback traversal to the control that received the click.
                // Avoid sheet/table contents and never enumerate a whole application.
                int remaining=48;
                PixelRect? Visit(IAccessible node,int depth)
                {
                    if(remaining--<=0 || depth>12) return null;
                    try
                    {
                        var direct=ReadText(node);
                        if(direct is { Height: >1 }) return direct;
                        int role=Convert.ToInt32(node.get_accRole(0));
                        if(role is 2 or 9 or 12 or 24) return null;
                        node.accLocation(out _,out _,out int width,out int height,0);
                        if(height>250 || width<=0) return null;
                        int count=node.accChildCount;
                        if(count>16) return null;
                        for(int i=1;i<=count;i++)
                        {
                            var child=node.get_accChild(i); Track(child);
                            if(child is IAccessible next && !ReferenceEquals(node,next))
                            {
                                var found=Visit(next,depth+1);
                                if(found!=null) return found;
                            }
                        }
                    }
                    catch(COMException) { }
                    return null;
                }
                var result=Visit(root,0);
                trace.Add(result!=null ? "Clicked editor caret" : "Clicked control has no caret");
                return result;
            }

            IAccessible current=root;
            for(int depth=0;depth<12;depth++)
            {
                object? child;
                try { child=current.accFocus; }
                catch(COMException ex) when(ex.HResult==unchecked((int)0x80004005)) { break; }
                Track(child);
                if(child is int id && id!=0) { child=current.get_accChild(id); Track(child); }
                if(child is not IAccessible next || ReferenceEquals(next,current)) break;
                current=next;
            }
            int state=Convert.ToInt32(current.get_accState(0));
            if(requireFocused && (state & 4)==0) return null;
            var caret=ReadText(current);
            trace.Add(caret!=null ? "IA2 caret bounds" : "IA2 caret unavailable");
            return caret;
        }
        finally { for(int i=objects.Count-1;i>=0;i--) Marshal.ReleaseComObject(objects[i]); }
    }
}
