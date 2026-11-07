// Draws shared\nevatch.ico (the shield, at the sizes Windows uses) with the same code as the apps.
// Only needed if the shield changes. Build and run from this folder:
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:MakeIcon.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll MakeIcon.cs NevatchUi.cs
//   MakeIcon.exe
class MakeIcon
{
    static void Main()
    {
        Nevatch.Ui.Art.SaveIco("nevatch.ico", new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
    }
}
