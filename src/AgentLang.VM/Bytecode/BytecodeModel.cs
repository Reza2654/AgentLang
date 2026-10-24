using System.IO;
using System.Text;

namespace AgentLang.VM.Bytecode;

public enum AgentOpCode : byte
{
    Nop = 0,
    PushConst = 1,
    Pop = 2,
    LoadVar = 3,
    StoreVar = 4,
    Add = 5,
    Sub = 6,
    Mul = 7,
    Div = 8,
    CompareEq = 9,
    CompareLt = 10,
    CompareGt = 11,
    Jump = 12,
    JumpIfTrue = 13,
    JumpIfFalse = 14,
    CallAgent = 15,
    CallTool = 16,
    EmitEvent = 17,
    Print = 18,
    Return = 19,
    Halt = 20
}

public readonly record struct BytecodeInstruction(AgentOpCode OpCode, int Operand = 0, int Line = 1)
{
    public override string ToString() =>
        Operand != 0 ? $"{OpCode} {Operand} (line {Line})" : $"{OpCode} (line {Line})";
}

public sealed class AgentBytecodeModule
{
    public const uint Magic = 0x41475443; // "AGTC"
    public const ushort FormatVersion = 1;

    public string ModuleName { get; set; } = "Main";
    public List<object?> ConstantPool { get; } = new();
    public List<string> SymbolTable { get; } = new();
    public List<BytecodeInstruction> Instructions { get; } = new();

    public int AddConstant(object? value)
    {
        for (int i = 0; i < ConstantPool.Count; i++)
        {
            if (Equals(ConstantPool[i], value)) return i;
        }
        ConstantPool.Add(value);
        return ConstantPool.Count - 1;
    }

    public int AddSymbol(string symbol)
    {
        for (int i = 0; i < SymbolTable.Count; i++)
        {
            if (string.Equals(SymbolTable[i], symbol, StringComparison.Ordinal)) return i;
        }
        SymbolTable.Add(symbol);
        return SymbolTable.Count - 1;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // Header
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(ModuleName);

        // Constant Pool
        writer.Write(ConstantPool.Count);
        foreach (var c in ConstantPool)
        {
            if (c == null)
            {
                writer.Write((byte)0);
            }
            else if (c is string s)
            {
                writer.Write((byte)1);
                writer.Write(s);
            }
            else if (c is double d)
            {
                writer.Write((byte)2);
                writer.Write(d);
            }
            else if (c is bool b)
            {
                writer.Write((byte)3);
                writer.Write(b);
            }
            else if (c is long l)
            {
                writer.Write((byte)4);
                writer.Write(l);
            }
            else if (c is int n)
            {
                writer.Write((byte)4);
                writer.Write((long)n);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(c.ToString() ?? "");
            }
        }

        // Symbol Table
        writer.Write(SymbolTable.Count);
        foreach (var sym in SymbolTable)
        {
            writer.Write(sym);
        }

        // Instructions
        writer.Write(Instructions.Count);
        foreach (var inst in Instructions)
        {
            writer.Write((byte)inst.OpCode);
            writer.Write(inst.Operand);
            writer.Write(inst.Line);
        }

        writer.Flush();
        return ms.ToArray();
    }

    public static AgentBytecodeModule FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException("Invalid AGTC magic header: file is not an AgentLang bytecode binary.");
        }

        ushort version = reader.ReadUInt16();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported AGTC bytecode format version: {version}");
        }

        var module = new AgentBytecodeModule
        {
            ModuleName = reader.ReadString()
        };

        // Read Constant Pool
        int constCount = reader.ReadInt32();
        for (int i = 0; i < constCount; i++)
        {
            byte typeTag = reader.ReadByte();
            object? val = typeTag switch
            {
                0 => null,
                1 => reader.ReadString(),
                2 => reader.ReadDouble(),
                3 => reader.ReadBoolean(),
                4 => reader.ReadInt64(),
                _ => throw new InvalidDataException($"Unknown constant tag: {typeTag}")
            };
            module.ConstantPool.Add(val);
        }

        // Read Symbol Table
        int symCount = reader.ReadInt32();
        for (int i = 0; i < symCount; i++)
        {
            module.SymbolTable.Add(reader.ReadString());
        }

        // Read Instructions
        int instCount = reader.ReadInt32();
        for (int i = 0; i < instCount; i++)
        {
            var op = (AgentOpCode)reader.ReadByte();
            int operand = reader.ReadInt32();
            int line = reader.ReadInt32();
            module.Instructions.Add(new BytecodeInstruction(op, operand, line));
        }

        return module;
    }

    public void SaveToFile(string filePath) =>
        File.WriteAllBytes(filePath, ToBytes());

    public static AgentBytecodeModule LoadFromFile(string filePath) =>
        FromBytes(File.ReadAllBytes(filePath));
}
