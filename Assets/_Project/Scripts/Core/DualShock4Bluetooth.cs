using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// 藍牙連線的 PS4 手把（DualShock 4）震動。
/// Unity Input System 內建的 DualShock4GamepadHID.SetMotorSpeeds 只會送 USB 用的 report 0x05，
/// 藍牙連線時手把要的是 report 0x11（78 bytes，最後 4 bytes 是 CRC32），收到 0x05 會直接忽略，所以完全不會震。
/// 這裡照藍牙格式自己組指令送出（只開「震動」這一個旗標，不動手把燈條的顏色）。
///   第 0 byte 0x11、第 1 byte 0xC0（HID + CRC）、第 3 byte 旗標 0x01（震動）
///   第 6 byte 右邊的高頻小馬達、第 7 byte 左邊的低頻大馬達
///   CRC32 算的是「0xA2 + 前 74 bytes」，little endian 放在第 74~77 byte
/// 判斷藍牙：HID 描述的 inputReportSize 不是 USB 的 64（藍牙通常是 547 或 78）。
/// </summary>
public static class DualShock4Bluetooth
{
    const int ReportSize = 78;

    /// <summary>這支手把是藍牙連線的 PS4 手把嗎（USB 連線的交給 Unity 內建的震動就好）</summary>
    public static bool Is(Gamepad pad)
    {
        if (!(pad is UnityEngine.InputSystem.DualShock.DualShock4GamepadHID)) return false;
        if (pad.deviceId == cachedId) return cachedBluetooth;
        cachedId = pad.deviceId;
        var m = Regex.Match(pad.description.capabilities ?? "", @"""inputReportSize""\s*:\s*(\d+)");
        cachedBluetooth = m.Success && int.Parse(m.Groups[1].Value) != 64;
        return cachedBluetooth;
    }
    static int cachedId = -1;
    static bool cachedBluetooth;

    /// <summary>回傳 HID 後端的結果（負數 = 送失敗）</summary>
    public static long SetMotors(Gamepad pad, float lowFrequency, float highFrequency)
    {
        var report = new byte[ReportSize];
        report[0] = 0x11;
        report[1] = 0xC0;
        report[3] = 0x01;   // 只改震動（0x02 燈條、0x04 閃爍都不開，燈條顏色維持原樣）
        report[6] = (byte)Mathf.Clamp(Mathf.RoundToInt(highFrequency * 255f), 0, 255);
        report[7] = (byte)Mathf.Clamp(Mathf.RoundToInt(lowFrequency * 255f), 0, 255);
        uint crc = Crc32(0xA2, report, ReportSize - 4);
        report[74] = (byte)crc; report[75] = (byte)(crc >> 8); report[76] = (byte)(crc >> 16); report[77] = (byte)(crc >> 24);

        var command = new Command(report);
        return pad.ExecuteCommand(ref command);
    }

    static uint Crc32(byte seed, byte[] data, int length)
    {
        uint crc = 0xFFFFFFFF;
        crc = Step(crc, seed);
        for (int i = 0; i < length; i++) crc = Step(crc, data[i]);
        return ~crc;
    }

    static uint Step(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }

    /// <summary>送給 HID 後端的「寫 output report」指令（HIDO）。不用 unsafe：78 個 byte 一個一個排好</summary>
    [StructLayout(LayoutKind.Explicit, Size = InputDeviceCommandBase + ReportSize)]
    struct Command : IInputDeviceCommandInfo
    {
        const int InputDeviceCommandBase = 8;
        public static FourCC Type => new FourCC('H', 'I', 'D', 'O');
        public FourCC typeStatic => Type;

        [FieldOffset(0)] public InputDeviceCommand baseCommand;
        [FieldOffset(8)] long p0; [FieldOffset(16)] long p1; [FieldOffset(24)] long p2; [FieldOffset(32)] long p3; [FieldOffset(40)] long p4;
        [FieldOffset(48)] long p5; [FieldOffset(56)] long p6; [FieldOffset(64)] long p7; [FieldOffset(72)] long p8;
        [FieldOffset(80)] int p9; [FieldOffset(84)] short p10;

        public Command(byte[] report)
        {
            baseCommand = new InputDeviceCommand(Type, InputDeviceCommandBase + ReportSize);
            p0 = L(report, 0); p1 = L(report, 8); p2 = L(report, 16); p3 = L(report, 24); p4 = L(report, 32);
            p5 = L(report, 40); p6 = L(report, 48); p7 = L(report, 56); p8 = L(report, 64);
            p9 = (int)(L(report, 72) & 0xFFFFFFFF); p10 = (short)(L(report, 76) & 0xFFFF);
        }

        static long L(byte[] b, int at)
        {
            long v = 0;
            for (int i = 0; i < 8 && at + i < b.Length; i++) v |= (long)b[at + i] << (8 * i);
            return v;
        }
    }
}
