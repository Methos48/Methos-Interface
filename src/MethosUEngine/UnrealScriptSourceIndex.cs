namespace MethosUEngine;

public sealed record UnrealFunctionSource(string Name, string Body, int DeclarationOffset,
    int BodyOffset, int BodyLength, int Line);

/// <summary>
/// Locates UnrealScript function bodies while ignoring braces and keywords inside
/// comments, string literals, name literals and preprocessor lines.
/// This is a source indexer, not yet the full semantic compiler.
/// </summary>
public static class UnrealScriptSourceIndex
{
    private enum TokenKind { Identifier, Symbol, Literal }
    private readonly record struct Token(string Text, TokenKind Kind, int Start, int End, int Line);

    public static IReadOnlyList<UnrealFunctionSource> ParseFunctions(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Token> tokens = Tokenize(source);
        var functions = new List<UnrealFunctionSource>();

        for (int i = 0; i < tokens.Count; i++)
        {
            Token keyword = tokens[i];
            if (keyword.Kind != TokenKind.Identifier ||
                !(keyword.Text.Equals("function", StringComparison.OrdinalIgnoreCase) ||
                  keyword.Text.Equals("event", StringComparison.OrdinalIgnoreCase)))
                continue;

            int openParen = -1;
            for (int j = i + 1; j < tokens.Count && j < i + 96; j++)
            {
                if (tokens[j].Text == "(") { openParen = j; break; }
                if (tokens[j].Text is ";" or "{" or "}") break;
            }
            if (openParen <= i + 1) continue;

            Token nameToken = tokens[openParen - 1];
            if (nameToken.Kind == TokenKind.Literal || nameToken.Text.Length == 0) continue;

            int depth = 0;
            int closeParen = -1;
            for (int j = openParen; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "(") depth++;
                else if (tokens[j].Text == ")" && --depth == 0) { closeParen = j; break; }
            }
            if (closeParen < 0) continue;

            int bodyOpen = -1;
            for (int j = closeParen + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "{") { bodyOpen = j; break; }
                if (tokens[j].Text is ";" or "}") break;
            }
            if (bodyOpen < 0) { i = closeParen; continue; }

            Token open = tokens[bodyOpen];
            depth = 0;
            int bodyClose = -1;
            for (int j = bodyOpen; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "{") depth++;
                else if (tokens[j].Text == "}" && --depth == 0) { bodyClose = j; break; }
            }
            if (bodyClose < 0)
            {
                string excerpt = source.Substring(open.Start, Math.Min(900, source.Length - open.Start));
                throw new InvalidDataException($"La función '{nameToken.Text}' (línea {keyword.Line}) tiene un bloque sin cerrar; apertura en línea {open.Line}, offset {open.Start}. Fragmento: {excerpt}");
            }

            Token close = tokens[bodyClose];
            int bodyStart = open.End;
            int bodyEnd = close.Start;
            functions.Add(new UnrealFunctionSource(nameToken.Text,
                source.Substring(bodyStart, bodyEnd - bodyStart),
                keyword.Start, bodyStart, bodyEnd - bodyStart, keyword.Line));
            i = bodyClose;
        }

        return functions;
    }

    public sealed record ClosureRepair(string FunctionName, int Line, int ClosuresInserted, int ClosuresRemoved = 0);
    public sealed record ClosureRepairResult(string Source, IReadOnlyList<ClosureRepair> Repairs);

    /// <summary>
    /// Repairs only missing closing braces at the boundary before the next function/event.
    /// Intended for source emitted by a decompiler that failed while formatting nested scopes.
    /// The original source is never modified; callers receive a repaired copy and an audit list.
    /// </summary>
    public static ClosureRepairResult RepairMissingFunctionClosures(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Token> tokens = Tokenize(source);
        var headers = FindFunctionHeaders(tokens);
        var insertions = new List<(int Offset, string Name, int Line, int Count)>();

        for (int h = 0; h < headers.Count; h++)
        {
            var header = headers[h];
            if (header.BodyOpenIndex < 0) continue;

            int boundary = source.Length;
            for (int tokenIndex = header.BodyOpenIndex + 1; tokenIndex < tokens.Count; tokenIndex++)
            {
                if (IsDeclarationBoundary(tokens, tokenIndex))
                {
                    boundary = tokens[tokenIndex].Start;
                    break;
                }
            }
            int depth = 0;
            bool closed = false;

            for (int i = header.BodyOpenIndex; i < tokens.Count && tokens[i].Start < boundary; i++)
            {
                if (tokens[i].Text == "{") depth++;
                else if (tokens[i].Text == "}")
                {
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0) closed = true;
                    }
                    // An unmatched close is checked after missing closures have been repaired.
                    // It may be a genuine state/defaultproperties terminator outside this function.
                }
            }

            if (!closed && depth > 0)
                insertions.Add((boundary, header.Name, header.Line, depth));
        }

        string repaired = source;
        var repairs = new List<ClosureRepair>(insertions.Count);
        foreach (var item in insertions.OrderByDescending(x => x.Offset))
        {
            string closing = Environment.NewLine + new string('}', item.Count) + Environment.NewLine;
            repaired = repaired.Insert(item.Offset, closing);
            repairs.Add(new ClosureRepair(item.Name, item.Line, item.Count));
        }

        // Remove only unmatched class-level closing braces immediately before a real
        // declaration (or EOF). State and defaultproperties braces remain balanced.
        List<Token> repairedTokens = Tokenize(repaired);
        var orphanClosures = new List<(int Offset, int Line)>();
        int globalDepth = 0;
        for (int i = 0; i < repairedTokens.Count; i++)
        {
            if (repairedTokens[i].Text == "{") globalDepth++;
            else if (repairedTokens[i].Text == "}")
            {
                if (globalDepth > 0) globalDepth--;
                else orphanClosures.Add((repairedTokens[i].Start, repairedTokens[i].Line));
            }
        }
        foreach (var orphan in orphanClosures.OrderByDescending(item => item.Offset))
        {
            repaired = repaired.Remove(orphan.Offset, 1);
            repairs.Add(new ClosureRepair("orphan", orphan.Line, 0, 1));
        }

        repaired = RemoveRedundantTrailingBareReturns(repaired);

        // Fail closed if the repaired copy is still structurally incomplete.
        _ = ParseFunctions(repaired);
        repairs.Reverse();
        return new ClosureRepairResult(repaired, repairs);
    }

    private static string RemoveRedundantTrailingBareReturns(string source)
    {
        List<Token> tokens = Tokenize(source);
        var removals = new List<(int Start, int Length)>();
        foreach (var header in FindFunctionHeaders(tokens))
        {
            if (header.BodyOpenIndex < 0) continue;
            int depth = 0, bodyEnd = -1;
            for (int i = header.BodyOpenIndex; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "{") depth++;
                else if (tokens[i].Text == "}" && --depth == 0)
                {
                    bodyEnd = i;
                    break;
                }
            }
            if (bodyEnd < 0) continue;

            for (int i = header.BodyOpenIndex + 1; i + 2 <= bodyEnd; i++)
            {
                if (!tokens[i].Text.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                    tokens[i + 1].Text != ";" || i + 2 != bodyEnd ||
                    tokens[i - 1].Text != ";")
                    continue;

                int previousStatementStart = i - 2;
                while (previousStatementStart > header.BodyOpenIndex &&
                       tokens[previousStatementStart].Text is not (";" or "{" or "}"))
                    previousStatementStart--;
                previousStatementStart++;

                if (tokens[previousStatementStart].Text.Equals("return", StringComparison.OrdinalIgnoreCase) &&
                    previousStatementStart + 1 < i - 1)
                    removals.Add((tokens[i].Start, tokens[i + 1].End - tokens[i].Start));
            }
        }

        foreach (var removal in removals.OrderByDescending(item => item.Start))
            source = source.Remove(removal.Start, removal.Length);
        return source;
    }

    private static bool IsDeclarationBoundary(IReadOnlyList<Token> tokens, int index)
    {
        Token token = tokens[index];
        if (token.Kind != TokenKind.Identifier) return false;

        bool Is(string value, int at) =>
            at >= 0 && at < tokens.Count &&
            tokens[at].Text.Equals(value, StringComparison.OrdinalIgnoreCase);

        bool HasOpeningBraceBeforeTerminator(int start)
        {
            for (int i = start; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "{") return true;
                if (tokens[i].Text is ";" or "}") return false;
            }
            return false;
        }

        if (Is("function", index) || Is("event", index))
        {
            int open = -1;
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "(") { open = i; break; }
                if (tokens[i].Text is ";" or "{" or "}") return false;
            }
            if (open <= index + 1) return false;
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "(") depth++;
                else if (tokens[i].Text == ")" && --depth == 0)
                    return HasOpeningBraceBeforeTerminator(i + 1);
            }
            return false;
        }

        if (Is("state", index))
        {
            int cursor = index + 1;
            if (Is("(", cursor))
            {
                if (!Is(")", cursor + 1)) return false;
                cursor += 2;
            }
            if (cursor >= tokens.Count || tokens[cursor].Kind != TokenKind.Identifier)
                return false;
            cursor++;
            if (Is("extends", cursor))
            {
                cursor++;
                if (cursor >= tokens.Count || tokens[cursor].Kind != TokenKind.Identifier)
                    return false;
                cursor++;
            }
            return HasOpeningBraceBeforeTerminator(cursor);
        }

        if (Is("defaultproperties", index) || Is("replication", index) || Is("cpptext", index))
            return Is("{", index + 1);

        if (Is("struct", index) || Is("enum", index))
        {
            if (index + 1 >= tokens.Count || tokens[index + 1].Kind != TokenKind.Identifier)
                return false;
            return HasOpeningBraceBeforeTerminator(index + 2);
        }

        if (Is("const", index))
        {
            bool hasEquals = false;
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "=") hasEquals = true;
                if (tokens[i].Text == ";") return hasEquals;
                if (tokens[i].Text is "{" or "}") return false;
            }
            return false;
        }

        if (Is("var", index))
        {
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == ";") return true;
                if (tokens[i].Text is "{" or "}") return false;
            }
        }

        return false;
    }

    private sealed record FunctionHeader(int KeywordIndex, int BodyOpenIndex, string Name, int Line);

    private static List<FunctionHeader> FindFunctionHeaders(List<Token> tokens)
    {
        var headers = new List<FunctionHeader>();
        for (int i = 0; i < tokens.Count; i++)
        {
            Token keyword = tokens[i];
            if (keyword.Kind != TokenKind.Identifier ||
                !(keyword.Text.Equals("function", StringComparison.OrdinalIgnoreCase) ||
                  keyword.Text.Equals("event", StringComparison.OrdinalIgnoreCase)))
                continue;

            int openParen = -1;
            for (int j = i + 1; j < tokens.Count && j < i + 96; j++)
            {
                if (tokens[j].Text == "(") { openParen = j; break; }
                if (tokens[j].Text is ";" or "{" or "}") break;
            }
            if (openParen <= i + 1) continue;

            Token name = tokens[openParen - 1];
            if (name.Kind == TokenKind.Literal || name.Text.Length == 0) continue;

            int depth = 0, closeParen = -1;
            for (int j = openParen; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "(") depth++;
                else if (tokens[j].Text == ")" && --depth == 0) { closeParen = j; break; }
            }
            if (closeParen < 0) continue;

            int bodyOpen = -1;
            for (int j = closeParen + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "{") { bodyOpen = j; break; }
                if (tokens[j].Text is ";" or "}") break;
            }
            headers.Add(new FunctionHeader(i, bodyOpen, name.Text, keyword.Line));
        }
        return headers;
    }

    public sealed record RedundantReturnRepair(string FunctionName, int Line);
    public sealed record ReturnNormalizationResult(string Source, IReadOnlyList<RedundantReturnRepair> Repairs);

    /// <summary>
    /// Removes a trailing bare return when the same top-level function body already
    /// contains a preceding return statement. The trailing statement is unreachable.
    /// </summary>
    public static ReturnNormalizationResult RemoveRedundantTrailingReturns(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var edits = new List<(int Start, int Length, string Name, int Line)>();
        foreach (var function in ParseFunctions(source))
        {
            string body = source.Substring(function.BodyOffset, function.BodyLength);
            List<Token> tokens = Tokenize(body);
            if (tokens.Count < 4) continue;

            int lastReturn = tokens.Count - 2;
            if (tokens[lastReturn].Kind != TokenKind.Identifier ||
                !tokens[lastReturn].Text.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                tokens[lastReturn + 1].Text != ";")
                continue;

            int[] depths = new int[tokens.Count];
            int depth = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "{") depth++;
                else if (tokens[i].Text == "}") depth--;
                depths[i] = depth;
            }

            int previousReturn = -1;
            for (int i = lastReturn - 1; i >= 0; i--)
            {
                if (tokens[i].Kind == TokenKind.Identifier &&
                    tokens[i].Text.Equals("return", StringComparison.OrdinalIgnoreCase) &&
                    depths[i] == depths[lastReturn])
                {
                    previousReturn = i;
                    break;
                }
            }

            if (previousReturn < 0 || previousReturn + 1 >= lastReturn ||
                tokens[previousReturn + 1].Text == ";")
                continue;

            bool conditionalReturn = false;
            for (int i = 0; i < previousReturn; i++)
            {
                if (depths[i] == depths[previousReturn] &&
                    tokens[i].Kind == TokenKind.Identifier &&
                    tokens[i].Text is "if" or "else" or "switch" or "while" or "for" or "foreach" or "do")
                {
                    conditionalReturn = true;
                    break;
                }
            }
            if (conditionalReturn) continue;

            int start = function.BodyOffset + tokens[lastReturn].Start;
            int end = function.BodyOffset + tokens[lastReturn + 1].End;
            edits.Add((start, end - start, function.Name, function.Line));
        }

        string normalized = source;
        var repairs = new List<RedundantReturnRepair>(edits.Count);
        foreach (var edit in edits.OrderByDescending(item => item.Start))
        {
            normalized = normalized.Remove(edit.Start, edit.Length);
            repairs.Add(new RedundantReturnRepair(edit.Name, edit.Line));
        }
        repairs.Reverse();
        return new ReturnNormalizationResult(normalized, repairs);
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0, line = 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                int commentDepth = 1;
                while (i < source.Length && commentDepth > 0)
                {
                    if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                    {
                        commentDepth++; i += 2; continue;
                    }
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        commentDepth--; i += 2; continue;
                    }
                    if (source[i] == '\n') line++;
                    i++;
                }
                if (commentDepth != 0) throw new InvalidDataException("Comentario de bloque sin cerrar.");
                continue;
            }
            if (c == '#')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '"' && i + 2 < source.Length &&
                source[i + 1] == '"' && source[i + 2] == '"')
            {
                int start = i;
                i += 3;
                tokens.Add(new Token(source[start..i], TokenKind.Literal, start, i, line));
                continue;
            }
            if (c is '"' or '\'')
            {
                char quote = c;
                int start = i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        if (source[i + 1] == '\n') line++;
                        i += 2;
                        continue;
                    }
                    if (source[i] == '\n') line++;
                    if (source[i++] == quote) break;
                }
                tokens.Add(new Token(source[start..i], TokenKind.Literal, start, i, line));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(source[start..i], TokenKind.Identifier, start, i, line));
                continue;
            }

            int symbolStart = i;
            string symbol;
            if (i + 2 < source.Length && source.AsSpan(i, 3).SequenceEqual(">>>"))
            { symbol = ">>>"; i += 3; }
            else if (i + 1 < source.Length && new[] { "++", "--", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=", "@=", "$=", "&=", "|=", "^=", "<<", ">>", "**", "~=", "^^" }.Contains(source.Substring(i, 2)))
            { symbol = source.Substring(i, 2); i += 2; }
            else { symbol = source[i++].ToString(); }
            tokens.Add(new Token(symbol, TokenKind.Symbol, symbolStart, i, line));
        }
        return tokens;
    }
}
