using NormalKeyboardSwitcher2;
int passed=0;
void Check(bool result,string name) { if(!result) throw new Exception("FAIL: "+name); passed++; Console.WriteLine("PASS: "+name); }
var recent=new RecentLayouts();
recent.Refresh(new nint[]{1,2,3,4});
Check(recent.Next(1)==2,"first gesture selects previous language");
Check(recent.Next(2)==3,"held gesture uses fixed order despite preview");
Check(recent.Next(3)==4,"four languages are reachable");
recent.End(4);
Check(recent.Order.SequenceEqual(new nint[]{4,1,2,3}),"commit promotes only selected language");
Check(recent.Next(4)==1,"next gesture returns to previously active language");
recent.End(1);
Check(recent.Next(1)==4,"quick gestures toggle the two most recent");
recent.End(1);
Check(recent.Order[0]==1,"failed switch commits actual language");
recent.Refresh(new nint[]{1,3,5});
Check(recent.Order.SequenceEqual(new nint[]{1,3,5}),"removal and installation preserve relative recency");
recent.Observe(3);
Check(recent.Order[0]==3,"external changes update recency");
var empty=new RecentLayouts();
Check(empty.Next(0)==0,"empty installed list does not divide by zero");
empty.End(0);
var single=new RecentLayouts(); single.Refresh(new nint[]{9});
Check(single.Next(9)==9,"single layout is safe");
var state=new GestureState();
Check(state.Feed(0xA2,true)==GestureAction.None,"Ctrl alone does not switch");
Check(state.Feed(0xA0,true)==GestureAction.None,"defer selection until chord release");
Check(state.Feed(0xA0,false)==GestureAction.Next,"Shift tap selects next");
state.Feed(0xA0,true);
Check(state.Feed(0xA0,false)==GestureAction.Next,"hold Ctrl and tap Shift cycles");
Check(state.Feed(0xA2,false)==GestureAction.Commit,"release Ctrl commits");
state.Reset(); state.Feed(0xA2,true); state.Feed(0xA0,true);
Check(state.Feed(0x43,true)==GestureAction.None,"Ctrl Shift C does not switch");
state.Feed(0x43,false);
Check(state.Feed(0xA0,false)==GestureAction.None,"shortcut modifier release does not switch");
Check(state.Feed(0xA2,false)==GestureAction.None,"shortcut does not commit");
state.Reset(); state.Feed(0xA0,true); state.Feed(0xA2,true);
Check(state.Feed(0xA2,false)==GestureAction.Next,"reverse modifier press order supported");
Check(state.Feed(0xA0,false)==GestureAction.Commit,"reverse order commits");
state.Reset(); state.Feed(0xA2,true); state.Feed(0xA0,true);
Check(state.Feed(0xA0,true)==GestureAction.None,"auto-repeat does not advance");
state.Reset(); state.Gesture=Gesture.AltShift;
state.Feed(0xA4,true); state.Feed(0xA0,true);
Check(state.Feed(0xA0,false)==GestureAction.Next,"left Alt Shift works");
Check(state.Feed(0xA4,false)==GestureAction.Commit,"Alt release commits");
state.Reset(); state.Feed(0xA5,true); state.Feed(0xA0,true);
Check(state.Feed(0xA0,false)==GestureAction.None,"AltGr is not a language gesture");
var p=BadgePlacement.Place(new(-1900,100,2,20),new(-1920,0,1920,1080),54,27,5);
Check(p.X==-1893 && p.Y==111,"negative monitor coordinates preserved");
p=BadgePlacement.Place(new(1910,1070,2,20),new(0,0,1920,1080),54,27,5);
Check(p.X==1866 && p.Y==1053,"badge clamps at the screen edge");
p=BadgePlacement.Place(new(100,100,2,20),new(0,0,300,200),108,54,10);
Check(p.X==112 && p.Y==102,"scaled badge fits working area");
var original=new Dictionary<string,string> { ["Hotkey"]="2",["Language Hotkey"]="2",["Layout Hotkey"]="1" };
var backup=ShortcutPolicy.ToDisable(original,Gesture.CtrlShift);
Check(backup.Count==2 && !backup.ContainsKey("Layout Hotkey"),"disable only matching Windows gesture");
Check(original["Hotkey"]=="2","planning does not mutate original settings");
var current=new Dictionary<string,string>(original) { ["Hotkey"]="3",["Language Hotkey"]="3" };
var restore=ShortcutPolicy.ToRestore(current,backup);
Check(restore.Count==2 && restore["Hotkey"]=="2","restore exact previous gesture");
current["Language Hotkey"]="1";
restore=ShortcutPolicy.ToRestore(current,backup);
Check(restore.Count==1 && !restore.ContainsKey("Language Hotkey"),"preserve external Windows setting changes");
Check(ShortcutPolicy.ToDisable(original,Gesture.AltShift).Count==1,"Alt Shift only claims its own setting");
Check(ShortcutPolicy.ToDisable(new Dictionary<string,string>(),Gesture.CtrlShift).Count==0,"absent settings are not invented");
bool rejected=false;
try { ShortcutPolicy.ToRestore(current,new Dictionary<string,string>{{"Unrelated","2"}}); }
catch(ArgumentException) { rejected=true; }
Check(rejected,"reject invalid recovery journal keys");
state.Reset(); state.Gesture=Gesture.System;
Check(state.Feed(0xA2,true)==GestureAction.None && state.Feed(0xA0,true)==GestureAction.None &&
    state.Feed(0xA0,false)==GestureAction.None && state.Feed(0xA2,false)==GestureAction.None,
    "System mode leaves Ctrl Shift to Windows");
Check(state.Feed(0xA4,true)==GestureAction.None && state.Feed(0xA0,true)==GestureAction.None &&
    state.Feed(0xA0,false)==GestureAction.None && state.Feed(0xA4,false)==GestureAction.None,
    "System mode leaves Alt Shift to Windows");
Check(ShortcutPolicy.ToDisable(original,Gesture.System).Count==0,"System mode does not claim Windows shortcuts");
Check((int)Gesture.CtrlShift==0 && (int)Gesture.AltShift==1,"existing saved gesture values remain compatible");
uint[] white={0xFFFFFF,0xFFFFFF,0xFFFFFF,0xFFFFFF,0xFFFFFF};
uint[] black={0,0,0,0,0};
uint[] green={0x009000,0x009000,0x009000,0x009000,0x009000};
Check(CaretInkPolicy.Choose(new[]{black},white,null)==0,"black caret ink on a white field");
Check(CaretInkPolicy.Choose(new[]{white},black,null)==0xFFFFFF,"white caret ink on a dark field");
Check(CaretInkPolicy.Choose(new[]{green},white,null)==0x009000,"colored caret ink is preserved");
Check(CaretInkPolicy.Choose(new[]{white},white,null)==0,"blink-off fallback is black on white");
Check(CaretInkPolicy.Choose(new[]{black},black,null)==0xFFFFFF,"blink-off fallback is white on dark");
Check(CaretInkPolicy.Choose(new[]{white},white,0x009000)==0x009000,"colored caret survives its blink-off phase");
Check(CaretInkPolicy.Choose(new[]{white},white,0xFFFFFF)==0,"white ink is not retained when switching to a light field");
var cell=ConsoleGeometry.Caret(-1000,50,12,102,2,100,81,124,8,16,25);
Check(cell==new PixelRect(-920,94,8,4),"console cursor accounts for viewport scroll and underline height");
Check(ConsoleGeometry.Caret(0,0,1,99,0,100,79,124,8,16,25)==null,"scrolled-out console caret is hidden");
var blinking=new CaretBlink();
blinking.Restart(100);
Check(blinking.Visible(100,500),"language change starts visible immediately");
Check(blinking.Visible(1099,500),"language change holds visibility for one second");
Check(!blinking.Visible(1350,500),"twice-speed blink switches off");
Check(blinking.Visible(1600,500),"twice-speed blink switches on");
blinking.Restart(1400);
Check(blinking.Visible(1400,500),"switching language interrupts a hidden phase");
Check(blinking.Visible(1500,uint.MaxValue),"disabled system blinking stays visible");
Check(CaretInkPolicy.HasStroke(new[]{black},white),"detect visible dark caret");
Check(!CaretInkPolicy.HasStroke(new[]{white},white),"detect caret blink-off on light field");
uint[] gray={0x777777,0x777777,0x777777,0x777777,0x777777};
Check(CaretInkPolicy.Choose(new[]{gray},white,0)==0,"lighter caret samples do not dim black indicator");
Check(CaretInkPolicy.Choose(new[]{black},white,0x777777)==0,"strong black stroke improves initial gray sample");
Check(CaretInkPolicy.Choose(new[]{gray},black,0xFFFFFF)==0xFFFFFF,"dimmer samples do not dim white indicator");
Check(CaretInkPolicy.Choose(new[]{white},black,0)==0xFFFFFF,"background change still switches ink for contrast");
var emptyCalcCaret=new PixelRect(373,395,1,1);
var expandedCalcCaret=CaretGeometry.ExpandPlaceholder(emptyCalcCaret,new(373,395,1,16));
Check(expandedCalcCaret==new PixelRect(373,395,1,16),"empty Calc caret uses focused caret object height");
Check(BadgePlacement.Place(expandedCalcCaret,new(0,0,1920,1080),24,14,1).Y==406,"empty Calc badge sits beside lower caret");
Check(CaretGeometry.ExpandPlaceholder(emptyCalcCaret,new(373,395,80,16))==emptyCalcCaret,"whole cell bounds cannot become caret height");
Check(CaretGeometry.ExpandPlaceholder(emptyCalcCaret,new(473,395,1,16))==emptyCalcCaret,"unrelated control bounds do not move caret");
Check(CaretGeometry.ExpandPlaceholder(expandedCalcCaret,new(373,395,1,25))==expandedCalcCaret,"valid text caret geometry is preserved");
Check(CaretGeometry.Contains(new(375,406,24,14),378,408),"sampler excludes its own overlay pixels");
Check(!CaretGeometry.Contains(new(375,406,24,14),373,408),"sampler can still see the real caret");
var headerBlue=new uint[]{0xD77800,0xD77800,0xD77800,0xD77800,0xD77800};
var cellWhite=new uint[]{0xFFFFFF,0xFFFFFF,0xFFFFFF};
var cellBackground=CaretInkPolicy.Background(headerBlue,cellWhite);
Check(cellBackground.SequenceEqual(cellWhite),"A1 background ignores colored row header");
Check(!CaretInkPolicy.HasStroke(new[]{white},cellBackground),"white empty cell is not mistaken for caret ink");
Check(CaretInkPolicy.Choose(new[]{black},cellBackground,0xFFFFFF)==0,"real black caret replaces stale white ink in A1");
Check(CaretInkPolicy.Background(white,Array.Empty<uint>()).SequenceEqual(white),"left background remains fallback when right side is excluded");
Check(CaretInkPolicy.Background(new uint[]{0},new uint[]{0xFFFFFF}).Length==0,"insufficient background samples cannot change color");
Console.WriteLine(passed+" checks passed.");








