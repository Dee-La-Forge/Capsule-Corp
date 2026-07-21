using System.Runtime.InteropServices;

namespace UMP.Shared;

public static class JoystickService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public int dwSize;
        public int dwFlags;
        public int dwXpos, dwYpos, dwZpos;
        public int dwRpos, dwUpos, dwVpos;
        public int dwButtons;
        public int dwButtonNumber;
        public int dwPOV;
        public int dwReserved1, dwReserved2;
    }

    private const int JOY_RETURNBUTTONS = 0x80;
    private const int JOYERR_NOERROR = 0;

    [DllImport("winmm.dll")]
    private static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

    [DllImport("winmm.dll")]
    private static extern int joyGetNumDevs();

    /// <summary>Nombre de joysticks supportes par le systeme</summary>
    public static int GetDeviceCount() => joyGetNumDevs();

    /// <summary>Retourne le masque de boutons appuyes pour un joystick (0 = aucun bouton)</summary>
    public static int GetButtons(int joystickId)
    {
        try
        {
            var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
            return joyGetPosEx(joystickId, ref info) == JOYERR_NOERROR ? info.dwButtons : 0;
        }
        catch { return 0; }
    }

    /// <summary>Verifie si un joystick est connecte</summary>
    public static bool IsConnected(int joystickId)
    {
        var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
        return joyGetPosEx(joystickId, ref info) == JOYERR_NOERROR;
    }

    /// <summary>Retourne l'index du premier bouton appuye (-1 si aucun)</summary>
    public static int GetFirstPressedButton(int joystickId)
    {
        var buttons = GetButtons(joystickId);
        if (buttons == 0) return -1;
        for (int i = 0; i < 32; i++)
            if ((buttons & (1 << i)) != 0) return i;
        return -1;
    }

    /// <summary>Scanne tous les joysticks et retourne (joystickId, buttonIndex) du premier bouton appuye</summary>
    public static (int joystickId, int buttonIndex)? ScanAllButtons()
    {
        var count = Math.Min(GetDeviceCount(), 16);
        for (int j = 0; j < count; j++)
        {
            var btn = GetFirstPressedButton(j);
            if (btn >= 0) return (j, btn);
        }
        return null;
    }
}
