using System.IO;
using System.Text;

namespace AgentLang.VM.Bytecode;

/// <summary>
/// Provides human-readable disassembly and inspection for compiled AgentLang bytecode (.agtc) binaries.
/// </summary>
public static class BytecodeDisassembler
{
    public static string Disassemble(AgentBytecodeModule module)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        DisassembleTo(module, writer);
        return sb.ToString();
    }

    public static void DisassembleTo(AgentBytecodeModule module, TextWriter writer)
    {
        writer.WriteLine("================================================================================");
        writer.WriteLine($"AGENTLANG BYTECODE DISASSEMBLY (.agtc v{AgentBytecodeModule.FormatVersion})");
        writer.WriteLine($"Module: {module.ModuleName} | Magic: 0x{AgentBytecodeModule.Magic:X8} | Version: {AgentBytecodeModule.FormatVersion}");
        writer.WriteLine($"Instructions: {module.Instructions.Count} | Constants: {module.ConstantPool.Count} | Symbols: {module.SymbolTable.Count}");
        writer.WriteLine("================================================================================");
        writer.WriteLine();

        // 1. Constant Pool
        writer.WriteLine($"--- CONSTANT POOL ({module.ConstantPool.Count} entries) ---");
        for (int i = 0; i < module.ConstantPool.Count; i++)
        {
            var val = module.ConstantPool[i];
            string typeName = val switch
            {
                null => "null",
                string => "String",
                double => "Double",
                bool => "Boolean",
                long => "Int64",
                int => "Int32",
                _ => val.GetType().Name
            };
            string displayVal = val switch
            {
                null => "null",
                string s => $"\"{EscapeString(s)}\"",
                bool b => b ? "true" : "false",
                _ => val.ToString() ?? ""
            };
            writer.WriteLine($"  #{i,-4} [{typeName,-7}]  {displayVal}");
        }
        writer.WriteLine();

        // 2. Symbol Table
        writer.WriteLine($"--- SYMBOL TABLE ({module.SymbolTable.Count} entries) ---");
        for (int i = 0; i < module.SymbolTable.Count; i++)
        {
            writer.WriteLine($"  @{i,-4}  \"{module.SymbolTable[i]}\"");
        }
        writer.WriteLine();

        // 3. Instructions
        writer.WriteLine("--- INSTRUCTION STREAM ---");
        writer.WriteLine("  Offset  Line  OpCode              Operand   Annotation");
        writer.WriteLine("  ------------------------------------------------------------------------------");

        for (int i = 0; i < module.Instructions.Count; i++)
        {
            var inst = module.Instructions[i];
            string annotation = GetAnnotation(module, inst);
            writer.WriteLine($"  {i:D4}    {inst.Line,-4}  {inst.OpCode,-18}  {inst.Operand,-8}  {annotation}");
        }
        writer.WriteLine("================================================================================");
    }

    private static string GetAnnotation(AgentBytecodeModule module, BytecodeInstruction inst)
    {
        return inst.OpCode switch
        {
            AgentOpCode.PushConst =>
                inst.Operand >= 0 && inst.Operand < module.ConstantPool.Count
                    ? $"#{inst.Operand} ({FormatConst(module.ConstantPool[inst.Operand])})"
                    : $"#{inst.Operand} [out of range]",

            AgentOpCode.LoadVar =>
                inst.Operand >= 0 && inst.Operand < module.SymbolTable.Count
                    ? $"@{inst.Operand} (load \"{module.SymbolTable[inst.Operand]}\")"
                    : $"@{inst.Operand} [out of range]",

            AgentOpCode.StoreVar =>
                inst.Operand >= 0 && inst.Operand < module.SymbolTable.Count
                    ? $"@{inst.Operand} (store \"{module.SymbolTable[inst.Operand]}\")"
                    : $"@{inst.Operand} [out of range]",

            AgentOpCode.GetMember =>
                inst.Operand >= 0 && inst.Operand < module.SymbolTable.Count
                    ? $".\"{module.SymbolTable[inst.Operand]}\""
                    : $"member @{inst.Operand}",

            AgentOpCode.CallFunc =>
                $"({inst.Operand} arguments)",

            AgentOpCode.CallAgent =>
                inst.Operand >= 0 && inst.Operand < module.SymbolTable.Count
                    ? $"agent \"{module.SymbolTable[inst.Operand]}\""
                    : $"agent @{inst.Operand}",

            AgentOpCode.CallTool =>
                inst.Operand >= 0 && inst.Operand < module.SymbolTable.Count
                    ? $"tool \"{module.SymbolTable[inst.Operand]}\""
                    : $"tool @{inst.Operand}",

            AgentOpCode.Jump or AgentOpCode.JumpIfTrue or AgentOpCode.JumpIfFalse =>
                $"-> offset {inst.Operand:D4}",

            AgentOpCode.MakeList =>
                $"[{inst.Operand} elements]",

            AgentOpCode.MakeMap =>
                $"{{{inst.Operand} key-value pairs}}",

            AgentOpCode.Halt =>
                "[end of execution]",

            _ => string.Empty
        };
    }

    private static string FormatConst(object? val)
    {
        return val switch
        {
            null => "null",
            string s => s.Length > 25 ? $"\"{EscapeString(s[..22])}...\"" : $"\"{EscapeString(s)}\"",
            bool b => b ? "true" : "false",
            _ => val.ToString() ?? ""
        };
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
