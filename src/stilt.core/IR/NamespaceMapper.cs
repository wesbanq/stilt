namespace stilt.IR
{
    public interface INamespaceMapper
    {
        string GetFunctionPath(Block block, Symbol? funcSymbol);
        string GetScoreboardName(VarSymbol var);
        string GetNamespace(Symbol symbol);
        string SanitizeIdentifier(string name);
    }

    public class NamespaceMapper : INamespaceMapper
    {
        private readonly Dictionary<Symbol, string> _namespaceCache = new();
        private readonly Dictionary<Block, string> _blockPaths = new();
        private readonly Dictionary<VarSymbol, string> _scoreboardNames = new();
        private readonly Dictionary<string, string> _moduleNamespaces = new();
        private int _blockCounter = 0;

        public NamespaceMapper()
        {
        }

        public string GetNamespace(Symbol symbol)
        {
            if (_namespaceCache.TryGetValue(symbol, out var cached))
                return cached;

            string ns;
            if (symbol.IsBuiltin)
            {
                ns = "stilt:builtin";
            }
            else if (symbol.IsTemp)
            {
                // For temp symbols, use parent scope's namespace
                // This will be resolved when we know the context
                ns = "stilt:temp";
            }
            else if (!string.IsNullOrEmpty(symbol.Source) && symbol.Source != Symbol.TempSource)
            {
                // Derive namespace from file path
                if (_moduleNamespaces.TryGetValue(symbol.Source, out var moduleNs))
                {
                    ns = moduleNs;
                }
                else
                {
                    // Use filename without extension as namespace
                    var fileName = Path.GetFileNameWithoutExtension(symbol.Source);
                    ns = SanitizeIdentifier(fileName);
                    _moduleNamespaces[symbol.Source] = ns;
                }
            }
            else
            {
                ns = "stilt";
            }

            _namespaceCache[symbol] = ns;
            return ns;
        }

        public string GetFunctionPath(Block block, Symbol? funcSymbol)
        {
            if (_blockPaths.TryGetValue(block, out var cached))
                return cached;

            string path;
            if (funcSymbol is not null)
            {
                var ns = GetNamespace(funcSymbol);
                var funcName = SanitizeIdentifier(funcSymbol.Name);
                path = $"{ns}:{funcName}";
            }
            else if (!string.IsNullOrEmpty(block.Name))
            {
                // Block has a name (e.g., "if_0_then", "loop_0_body")
                // Find parent namespace from context
                var ns = "stilt";  // Default, should be set from context
                path = $"{ns}:{block.Name}";
            }
            else
            {
                // Generate a unique name for anonymous blocks
                var blockId = _blockCounter++;
                path = $"stilt:block_{blockId}";
            }

            _blockPaths[block] = path;
            return path;
        }

        public string GetScoreboardName(VarSymbol var)
        {
            if (_scoreboardNames.TryGetValue(var, out var cached))
                return cached;

            var ns = GetNamespace(var);
            var varName = SanitizeIdentifier(var.Name);
            var name = $"{ns}:{varName}";
            
            _scoreboardNames[var] = name;
            return name;
        }

        public string SanitizeIdentifier(string name)
        {
            // Minecraft identifiers allow [a-z0-9_.-]
            // Convert to lowercase and replace invalid characters with underscore
            var sanitized = Regex.Replace(name, @"[^a-z0-9_.-]", "_", RegexOptions.IgnoreCase);
            sanitized = sanitized.ToLowerInvariant();
            
            // Ensure it doesn't start with a number
            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            return sanitized;
        }

        public void RegisterModuleNamespace(string filePath, string namespaceName)
        {
            _moduleNamespaces[filePath] = namespaceName;
        }

        public void RegisterBlockPath(Block block, string path)
        {
            _blockPaths[block] = path;
        }
    }
}
