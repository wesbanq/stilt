namespace stilt
{
	public class Linker
	{
		public ProgramArgs Args;
		public List<Scope> Modules = [];
		public List<List<Stmt>> Trees = [];
		public List<CompilationMessage> Errors = [];

		private Dictionary<string, Scope> _loadedModules = [];

		public void Link()
		{
			// Errors.Clear();
			for (int i = 0; i < Trees.Count; i++)
			{
				var scope = i < Modules.Count ? Modules[i] : null;
				var tree = Trees[i];
				if (tree is null) continue;

				foreach (var stmt in tree)
				{
					if (stmt is not null && scope is not null)
						ProcessStmt(stmt, scope);
				}
			}
		}

		private void ProcessStmt(Stmt stmt, Scope currentScope)
		{
			switch (stmt)
			{
				case CompoundStmt compound:
				{
					foreach (var s in compound.Statements)
					{
						if (s is not null)
							ProcessStmt(s, s.Scope);
					}
					break;
				}
				case IfStmt ifStmt:
				{
					ProcessExpr(ifStmt.Condition, ifStmt.Scope);
					ProcessStmt(ifStmt.NextIf, ifStmt.NextIf.Scope);
					if (ifStmt.NextElse is not null)
						ProcessStmt(ifStmt.NextElse, ifStmt.NextElse.Scope);
					break;
				}
				case PreconditionLoopStmt pre:
				{
					ProcessExpr(pre.Condition, pre.Scope);
					if (pre.Body is not null)
						ProcessStmt(pre.Body, pre.Body.Scope);
					break;
				}
				case PostconditionLoopStmt post:
				{
					ProcessExpr(post.Condition, post.Scope);
					if (post.Body is not null)
						ProcessStmt(post.Body, post.Body.Scope);
					break;
				}
				case ForLoopStmt forLoop:
				{
					if (forLoop.LoopVariable is not null)
						ProcessStmt(forLoop.LoopVariable, forLoop.LoopVariable.Scope);
					ProcessExpr(forLoop.Condition, forLoop.Scope);
					ProcessExpr(forLoop.Iterator, forLoop.Scope);
					if (forLoop.Body is not null)
						ProcessStmt(forLoop.Body, forLoop.Body.Scope);
					break;
				}
				case ForeachLoopStmt foreachLoop:
				{
					ProcessStmt(foreachLoop.LoopVariable, foreachLoop.LoopVariable.Scope);
					ProcessExpr(foreachLoop.Iterator, foreachLoop.Scope);
					if (foreachLoop.Body is not null)
						ProcessStmt(foreachLoop.Body, foreachLoop.Body.Scope);
					break;
				}
				case LoopStmt loop:
				{
					if (loop.Body is not null)
						ProcessStmt(loop.Body, loop.Body.Scope);
					break;
				}
				case ReturnStmt ret:
				{
					ProcessExpr(ret.Value, ret.Scope);
					break;
				}
				case ExpressionStmt exprStmt:
				{
					ProcessExpr(exprStmt.Expression, exprStmt.Scope);
					break;
				}
                case VarDeclStmt varDecl:
				{
					ProcessExpr(varDecl.Value, varDecl.Scope);
					break;
				}
                case DeclStmt decl:
                {
                    if (decl.Value is not null)
						ProcessStmt(decl.Value, decl.Value.Scope);
                    break;
                }
				case ExecuteStmt exec:
				{
					if (exec.Executor is not null && exec.Executor.IsTemp)
						ResolveExecuteExecutor(exec, currentScope);
					break;
				}
				case ImportStmt import:
				{
					ProcessImport(import, currentScope);
					break;
				}
			}
		}

		private void ProcessExpr(Expr? expr, Scope currentScope)
		{
			if (expr is null) return;

			switch (expr)
			{
				case IdentityExpr id:
				{
					if (id.Identity.IsTemp)
						ResolveIdentity(id, currentScope, wantType: false);
					break;
				}

				case AccessExpr:
				case NullAccessExpr:
				{
					ResolveAccessChain((CommaExpr)expr, currentScope);
					break;
				}

				case UnaryExpr unary:
				{
					ProcessExpr(unary.Leaf, currentScope);
					break;
				}

				case BinaryExpr binary:
				{
					ProcessExpr(binary.Left, currentScope);
					ProcessExpr(binary.Right, currentScope);
					break;
				}

				case TernaryExpr ternary:
				{
					ProcessExpr(ternary.Left, currentScope);
					ProcessExpr(ternary.Middle, currentScope);
					ProcessExpr(ternary.Right, currentScope);
					break;
				}

				case CommaExpr comma:
				{
					foreach (var e in comma.Exprs)
						ProcessExpr(e, currentScope);
					break;
				}

				case ArrayLiteralExpr arr:
				{
					if (arr.Value is List<Expr> list)
					{
						foreach (var e in list)
							ProcessExpr(e, currentScope);
					}
					break;
				}

				case TableLiteralExpr tbl:
				{
					if (tbl.Value is Dictionary<Symbol, Expr> dict)
					{
						var newDict = new Dictionary<Symbol, Expr>();
						foreach (var kv in dict)
						{
							var key = kv.Key;
							if (key.IsTemp)
							{
								var resolved = currentScope.FindVarByName(key.Name)
									?? currentScope.FindTypeByName(key.Name) as Symbol;
								if (resolved is not null)
									key = resolved;
								else
								{
									var range = key.Identifier?.Range;
									if (range is not null)
										Errors.Add(new UndefinedSymbolError(range, key));
								}
							}
							ProcessExpr(kv.Value, currentScope);
							newDict[key] = kv.Value;
						}
						tbl.Value = newDict;
					}
					break;
				}

				case LambdaFuncExpr lambda:
				{
					if (lambda.Value is not null)
						ProcessStmt(lambda.Value, lambda.Value.Scope);
					break;
				}
			}
		}

		private void ResolveIdentity(IdentityExpr id, Scope scope, bool wantType)
		{
			Symbol? found = wantType
				? scope.FindTypeByName(id.Identity.Name)
				: scope.FindVarByName(id.Identity.Name);

			if (found is null)
				found = wantType
					? scope.FindVarByName(id.Identity.Name) as Symbol
					: scope.FindTypeByName(id.Identity.Name) as Symbol;

			if (found is not null)
			{
				id.Identity = found;
				return;
			}

			var range = id.Identity.Identifier?.Range ?? id.FullRange ?? id.InnerRange;
			if (range is not null)
				Errors.Add(new UndefinedSymbolError(range, id.Identity));
		}

		private void ResolveAccessChain(CommaExpr access, Scope currentScope)
		{
			if (access.Exprs.Count == 0) return;

			// Exprs order: [rightmost, ..., leftmost] e.g. a.b.c -> [c, b, a]; receiver is last
			var exprs = access.Exprs;
			IReadOnlyList<Symbol>? container = null;
			Symbol? current = null;

			for (int i = exprs.Count - 1; i >= 0; i--)
			{
				var seg = exprs[i];

				if (seg is IdentityExpr id)
				{
					string name = id.Identity.Name;

					if (container is null)
					{
						// First segment: resolve from scope; prefer TypeSymbol (static) then VarSymbol (instance)
						var typeSym = currentScope.FindTypeByName(name);
						if (typeSym is not null)
						{
							current = typeSym;
							container = typeSym.Members;
							id.Identity = typeSym;
							continue;
						}

						var varSym = currentScope.FindVarByName(name);
						if (varSym is not null)
						{
							current = varSym;
							// Check if this is an imported module
							if (varSym.Declaration is ImportStmt importStmt && importStmt.ImportedScope is not null)
								container = importStmt.ImportedScope.Symbols;
							else
								container = varSym.Type.Members;
							id.Identity = varSym;
							continue;
						}

						var range = id.Identity.Identifier?.Range ?? id.FullRange ?? id.InnerRange;
						if (range is not null)
							Errors.Add(new UndefinedSymbolError(range, id.Identity));
						return;
					}

					// Later segment: look up in container
					var member = container.FirstOrDefault(s => s.Name == name);
					if (member is not null)
					{
						current = member;
						id.Identity = member;
						container = member is TypeSymbol ts ? ts.Members
							: member is VarSymbol vs ? vs.Type.Members
							: null;
						continue;
					}

					var segRange = id.Identity.Identifier?.Range ?? id.FullRange ?? id.InnerRange;
					if (segRange is not null)
						Errors.Add(new UndefinedSymbolError(segRange, id.Identity));
					return;
				}

				if (seg is CommaExpr nested)
				{
					ResolveAccessChain(nested, currentScope);
					var tipId = GetTipIdentityInChain(nested);
					if (tipId is not null)
					{
						current = tipId.Identity;
						container = current is TypeSymbol ts ? ts.Members
							: current is VarSymbol vs ? vs.Type.Members
							: null;
					}
					else
						return;
				}
			}
		}

		// Gets the leftmost (tip) IdentityExpr in an access chain - the result of the access.
		private static IdentityExpr? GetTipIdentityInChain(CommaExpr comma)
		{
			if (comma.Exprs.Count == 0) return null;
			var first = comma.Exprs[0];
			return first as IdentityExpr ?? (first is CommaExpr c ? GetTipIdentityInChain(c) : null);
		}

		private void ResolveExecuteExecutor(ExecuteStmt exec, Scope currentScope)
		{
			var name = exec.Executor!.Name;
			var found = currentScope.FindVarByName(name);
			if (found is not null)
				exec.Executor = found;
			else
			{
				var range = exec.Executor.Identifier?.Range ?? exec.FullRange ?? exec.InnerRange;
				if (range is not null)
					Errors.Add(new UndefinedSymbolError(range, exec.Executor));
			}
		}

		private void ProcessImport(ImportStmt import, Scope currentScope)
		{
			var normalizedPath = Path.GetFullPath(import.Filepath);
			if (_loadedModules.TryGetValue(normalizedPath, out var cachedScope))
			{
				import.ImportedScope = cachedScope;
				return;
			}
			if (!File.Exists(normalizedPath))
			{
				if (string.IsNullOrEmpty(Path.GetExtension(normalizedPath)))
				{
					var pathWithExt = normalizedPath + Program.CodeFileExtension;
					if (File.Exists(pathWithExt))
					{
						normalizedPath = pathWithExt;
						import.Filepath = pathWithExt;
					}
					else
					{
						var range = import.InnerRange;
						if (range is not null)
							Errors.Add(new SyntaxError(range, $"Cannot import '{import.Filepath}': file not found"));
						return;
					}
				}
				else
				{
					var range = import.InnerRange;
					if (range is not null)
						Errors.Add(new SyntaxError(range, $"Cannot import '{import.Filepath}': file not found"));
					return;
				}
			}
			try
			{
				// Parse the imported file
				var file = Compiler.ParseFile(Args, normalizedPath);

				// Add parser errors to our error list
				Errors.AddRange(file.ParserResult!.CompilationIssues);

				// Create the imported scope (the parser's root scope contains all top-level symbols)
				import.ImportedScope = file.ParserResult!.RootScope;
				
				// Cache the loaded module
				_loadedModules[normalizedPath] = file.ParserResult!.RootScope;

				// Add the imported module to our Modules and Trees lists for further processing
				Modules.Add(file.ParserResult!.RootScope);
				Trees.Add(file.ParserResult!.Statements);

				// Recursively process the imported file's statements (for nested imports)
				foreach (var stmt in file.ParserResult!.Statements)
				{
					if (stmt is not null)
						ProcessStmt(stmt, stmt.Scope);
				}
			}
			catch (Exception ex)
			{
				var range = import.InnerRange;
				if (range is not null)
					Errors.Add(new SyntaxError(range, $"Error importing '{import.Filepath}': {ex.Message}"));
			}
		}

		public Linker(ProgramArgs args, List<Scope> modules, List<List<Stmt>> trees)
		{
			Modules = modules;
			Trees = trees;
			Args = args;
		}
	}
}
