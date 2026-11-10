using System.Text;

namespace MethosUEngine;

/// <summary>
/// Reads and writes the classic UE2 NTL native-function table format.
/// This file is the opcode dictionary used by the native compiler and decompiler.
/// </summary>
public sealed class UnrealScriptNativeFunctionCatalog
{
    private const uint Signature = 0x2C8D14F1;
    private const int MaximumEntries = 1_000_000;
    private readonly Dictionary<ushort, UnrealScriptNativeFunction> _byToken;
    private readonly Dictionary<string, UnrealScriptNativeFunction> _byName;

    public IReadOnlyCollection<UnrealScriptNativeFunction> Items => _byToken.Values;

    private UnrealScriptNativeFunctionCatalog(IEnumerable<UnrealScriptNativeFunction> items)
    {
        _byToken = new Dictionary<ushort, UnrealScriptNativeFunction>();
        _byName = new Dictionary<string, UnrealScriptNativeFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("La tabla NTL contiene una función sin nombre.");
            if (item.Token < 0x70 || item.Token >= 0x1000)
                throw new InvalidDataException($"Token nativo UE2 fuera de rango: 0x{item.Token:X}.");
            if (_byToken.TryGetValue(item.Token, out var existing))
            {
                if (existing != item)
                    throw new InvalidDataException($"Token nativo duplicado con definiciones distintas: 0x{item.Token:X}.");
                continue;
            }
            _byToken.Add(item.Token, item);
            _byName.TryAdd(item.Name, item);
        }
    }

    public static UnrealScriptNativeFunctionCatalog Create(IEnumerable<UnrealScriptNativeFunction> items) =>
        new(items ?? throw new ArgumentNullException(nameof(items)));

    public static UnrealScriptNativeFunctionCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static UnrealScriptNativeFunctionCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Signature)
            throw new InvalidDataException("El archivo no tiene una firma NTL válida.");
        int count = reader.ReadInt32();
        if (count < 0 || count > MaximumEntries)
            throw new InvalidDataException($"Cantidad de entradas NTL no válida: {count}.");

        var items = new List<UnrealScriptNativeFunction>(count);
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            byte precedence = reader.ReadByte();
            byte type = reader.ReadByte();
            int token = reader.ReadInt32();
            var kind = type switch
            {
                1 => UnrealScriptNativeFunctionKind.Function,
                2 => UnrealScriptNativeFunctionKind.Operator,
                3 => UnrealScriptNativeFunctionKind.PreOperator,
                4 => UnrealScriptNativeFunctionKind.PostOperator,
                _ => throw new InvalidDataException($"Tipo NTL desconocido {type} en entrada {i}.")
            };
            if (token < 0x70 || token >= 0x1000)
                throw new InvalidDataException($"Token NTL fuera de rango en entrada {i}: 0x{token:X}.");
            items.Add(new UnrealScriptNativeFunction((ushort)token, name, kind, precedence));
        }

        if (stream.CanSeek && stream.Position != stream.Length)
            throw new InvalidDataException("El archivo NTL contiene datos sobrantes.");
        return new UnrealScriptNativeFunctionCatalog(items);
    }

    public UnrealScriptNativeFunction? Resolve(ushort token) =>
        _byToken.TryGetValue(token, out var item) ? item : null;

    public UnrealScriptNativeFunction? FindByName(string name) =>
        _byName.TryGetValue(name, out var item) ? item : null;

    public void Save(string path)
    {
        using var stream = File.Create(path);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Signature);
        writer.Write(_byToken.Count);
        foreach (var item in _byToken.Values.OrderBy(value => value.Token))
        {
            writer.Write(item.Name);
            writer.Write(item.Precedence);
            writer.Write(item.Kind switch
            {
                UnrealScriptNativeFunctionKind.Function => (byte)1,
                UnrealScriptNativeFunctionKind.Operator => (byte)2,
                UnrealScriptNativeFunctionKind.PreOperator => (byte)3,
                UnrealScriptNativeFunctionKind.PostOperator => (byte)4,
                _ => throw new InvalidDataException("Tipo de función nativa no válido.")
            });
            writer.Write((int)item.Token);
        }
    }
}
