namespace MethosUEngine;

public enum UnrealScriptNativeFunctionKind
{
    Function,
    Operator,
    PreOperator,
    PostOperator
}

/// <summary>Native opcode metadata gathered from the game's native function table.</summary>
public sealed record UnrealScriptNativeFunction(ushort Token, string Name,
    UnrealScriptNativeFunctionKind Kind, byte Precedence = 0);
