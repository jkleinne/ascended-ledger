using System.Runtime.InteropServices;

using Dalamud.Memory;

namespace AscendedLedger.Services;

/// <summary>
/// Memory layout of one entry in the game's retainer sale-history packet,
/// as established by CashFlow (NightmareXIV). 52 bytes per entry; entries
/// start at packet offset 8 and terminate at ItemId 0.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
internal unsafe struct RetainerHistoryEntry {
    /// <summary>Size of one entry in bytes.</summary>
    public const int Size = 52;

    [FieldOffset(0)]
    public uint ItemId;

    [FieldOffset(4)]
    public uint Price;

    [FieldOffset(8)]
    public uint UnixTimeSeconds;

    [FieldOffset(12)]
    public uint Quantity;

    [FieldOffset(16)]
    public bool IsHq;

    [FieldOffset(18)]
    public bool IsMannequin;

    [FieldOffset(19)]
    public fixed byte BuyerNameBytes[BuyerNameLength];

    private const int BuyerNameLength = 32;

    /// <summary>Buyer name decoded up to the null terminator.</summary>
    public string BuyerName {
        get {
            fixed (byte* pointer = BuyerNameBytes) {
                return MemoryHelper.ReadString((nint)pointer, BuyerNameLength);
            }
        }
    }
}
