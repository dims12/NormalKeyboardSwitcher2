using System;
using System.Runtime.InteropServices;

namespace NormalKeyboardSwitcher2;

// Narrow COM declarations: unused slots preserve the Windows SDK vtable order.
// Do not call placeholder methods; expand their signatures before adding use.
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAutomation
{
    void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
    void ElementFromHandle(); void ElementFromPoint();
    IAutomationElement GetFocusedElement();
}
[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAutomationElement
{
    void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll();
    void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int property);
    void GetCurrentPropertyValueEx(); void GetCachedPropertyValue(); void GetCachedPropertyValueEx();
    void GetCurrentPatternAs(); void GetCachedPatternAs();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int pattern);
}
[ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextPattern2
{
    void RangeFromPoint(); void RangeFromChild(); void GetSelection(); void GetVisibleRanges();
    void GetDocumentRange(); void GetSupportedTextSelection(); void RangeFromAnnotation();
    ITextRange GetCaretRange([MarshalAs(UnmanagedType.Bool)] out bool active);
}
[ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextRange
{
    ITextRange Clone();
    [return: MarshalAs(UnmanagedType.Bool)] bool Compare(ITextRange range);
    int CompareEndpoints(int start,ITextRange other,int end);
    void ExpandToEnclosingUnit(int unit);
    void FindAttribute(); void FindText(); void GetAttributeValue();
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType=VarEnum.VT_R8)]
    double[] GetBoundingRectangles();
    void GetEnclosingElement(); void GetText(); void Move();
    int MoveEndpointByUnit(int endpoint,int unit,int count);
}


[ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextPattern
{
    void RangeFromPoint(); void RangeFromChild();
    ITextRangeArray GetSelection();
    void GetVisibleRanges(); void GetDocumentRange(); void GetSupportedTextSelection();
}
[ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextRangeArray
{
    int GetLength();
    ITextRange GetElement(int index);
}
