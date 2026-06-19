namespace stilt
{
	// INCOMPLETE: ported to the SymbolReference API so stilt.core compiles; the name-resolution logic itself is unfinished and untested.
	public class Linker(ProgramArgs args, Dictionary<string, ObjectFile> modules)
    {
		public ProgramArgs Args = args;
		public Dictionary<string, ObjectFile> Modules = modules;
		public List<CompilationMessage> Errors = [];

		public void Link()
		{
			foreach (var (_, file) in Modules)
			{
				foreach (var stmt in file.ParserResult!.Statements)
				{
					if (stmt is not null && file.ParserResult!.RootScope is not null)
						ProcessStmt(stmt);
				}
			}
		}

		private void ProcessStmt(Stmt stmt)
		{
			switch (stmt)
			{
				case CompoundStmt compound:
				{
					foreach (var s in compound.Statements)
					{
						if (s is not null)
							ProcessStmt(s);
					}

					break;
				}
				case IfStmt ifStmt:
				{
					ProcessExpr(ifStmt.Condition, ifStmt.Scope);
					ProcessStmt(ifStmt.NextIf);
					if (ifStmt.NextElse is not null)
						ProcessStmt(ifStmt.NextElse);

					break;
				}
				case PreconditionLoopStmt pre:
				{
					ProcessExpr(pre.Condition, pre.Scope);
					if (pre.Body is not null)
						ProcessStmt(pre.Body);

					break;
				}
				case PostconditionLoopStmt post:
				{
					ProcessExpr(post.Condition, post.Scope);
					if (post.Body is not null)
						ProcessStmt(post.Body);

					break;
				}
				case ForLoopStmt forLoop:
				{
					if (forLoop.LoopVariable is not null)
						ProcessStmt(forLoop.LoopVariable);
					ProcessExpr(forLoop.Condition, forLoop.Scope);
					ProcessExpr(forLoop.Iterator, forLoop.Scope);
					if (forLoop.Body is not null)
						ProcessStmt(forLoop.Body);

					break;
				}
				case ForeachLoopStmt foreachLoop:
				{
					ProcessStmt(foreachLoop.LoopVariable);
					ProcessExpr(foreachLoop.Iterator, foreachLoop.Scope);
					if (foreachLoop.Body is not null)
						ProcessStmt(foreachLoop.Body);

					break;
				}
				case LoopStmt loop:
				{
					if (loop.Body is not null)
						ProcessStmt(loop.Body);

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
				case ImportStmt import:
				{
					ProcessImport(import, import.Scope);
					
					break;
				}
                case DeclStmt decl:
                {
                    if (decl.Value is not null)
						ProcessStmt(decl.Value);
						
                    break;
                }
				case ExecuteStmt exec:
				{
					// if (exec.Executor is not null && exec.Executor.IsTemp)
					// 	ResolveExecuteExecutor(exec, currentScope);

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
					if (!id.Identity.IsResolved) 
						ResolveReference(id.Identity, currentScope);

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
				// case ArrayLiteralExpr arr:
				// {
				// 	if (arr.Value is List<Expr> list)
				// 	{
				// 		foreach (var e in list)
				// 			ProcessExpr(e, currentScope);
				// 	}
				// 	break;
				// }
				// case TableLiteralExpr tbl:
				// {
				// 	if (tbl.Value is Dictionary<Symbol, Expr> dict)
				// 	{
				// 		var newDict = new Dictionary<Symbol, Expr>();
				// 		foreach (var kv in dict)
				// 		{
				// 			var key = kv.Key;
				// 			if (key.IsTemp)
				// 			{
				// 				var resolved = currentScope.FindVarByName(key.Name)
				// 					?? currentScope.FindTypeByName(key.Name) as Symbol;
				// 				if (resolved is not null)
				// 					key = resolved;
				// 				else
				// 				{
				// 					var range = key.Token?.Range;
				// 					if (range is not null)
				// 						Errors.Add(new UndefinedSymbolError(range, key));
				// 				}
				// 			}
				// 			ProcessExpr(kv.Value, currentScope);
				// 			newDict[key] = kv.Value;
				// 		}
				// 		tbl.Value = newDict;
				// 	}
				// 	break;
				// }
				case FuncLiteralExpr lambda:
				{
					if (lambda.Value is not null)
						ProcessStmt(lambda.Value);
					break;
				}
			}
		}

		// Binds an identifier reference to the symbol it resolved to (no-op if already bound).
		private static void Bind(SymbolReference reference, Symbol symbol)
		{
			if (!reference.IsResolved)
				reference.Resolve(symbol);
		}

		// Members of a variable's resolved type, or null if its type isn't resolved to a TypeSymbol yet.
		private static IReadOnlyList<Symbol>? MembersOf(VarSymbol v) =>
			(v.Type.Resolved as TypeSymbol)?.Members;

		private void ResolveReference(SymbolReference symbol, Scope scope)
		{
			var name = symbol.Unresolved.Name;
			Symbol? found = scope.FindVarByName(name);
			found ??= scope.FindTypeByName(name);
			
			if (found is null)
			{
				Errors.Add(new UndefinedSymbolError(symbol.Unresolved.Token.Range, name));
				return;
			}
			
			Bind(symbol, found);
			if (found is TypeSymbol ts)
			{
				foreach (var arg in ts.Arguments)
					ResolveReference(arg, scope);
			}
		}

		private void ResolveAccessChain(CommaExpr access, Scope currentScope)
		{
			if (access.Exprs.Count == 0) return;

			// Exprs order: [rightmost, ..., leftmost] e.g. a.b.c -> [c, b, a]; receiver is last
			var exprs = access.Exprs;
			IReadOnlyList<Symbol>? container = null;
			Symbol? current = null;

			//TODO
			for (int i = exprs.Count - 1; i >= 0; i--)
			{
				var seg = exprs[i];

				if (seg is IdentityExpr id)
				{
					string name = id.Identity.Unresolved.Name;

					if (container is null)
					{
						// First segment: resolve from scope; prefer TypeSymbol (static) then VarSymbol (instance)
						var typeSym = currentScope.FindTypeByName(name);
						if (typeSym is not null)
						{
							current = typeSym;
							container = typeSym.Members;
							Bind(id.Identity, typeSym);
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
								container = MembersOf(varSym);
							Bind(id.Identity, varSym);
							continue;
						}

						var range = id.Identity.Unresolved.Token.Range;
						if (range is not null)
							Errors.Add(new UndefinedSymbolError(range, id.Identity.Unresolved.Name));
						return;
					}

					// Later segment: look up in container
					var member = container.FirstOrDefault(s => s.Name == name);
					if (member is not null)
					{
						current = member;
						Bind(id.Identity, member);
						container = member is TypeSymbol ts ? ts.Members
							: member is VarSymbol vs ? MembersOf(vs)
							: null;
						continue;
					}

					var segRange = id.Identity.Unresolved.Token.Range;
					if (segRange is not null)
						Errors.Add(new UndefinedSymbolError(segRange, id.Identity.Unresolved.Name));
					return;
				}

				if (seg is CommaExpr nested)
				{
					ResolveAccessChain(nested, currentScope);
					var tipId = GetTipIdentityInChain(nested);
					if (tipId is not null)
					{
						current = tipId.Identity.Resolved;
						container = current is TypeSymbol ts ? ts.Members
							: current is VarSymbol vs ? MembersOf(vs)
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

		// INCOMPLETE: resolving an `execute as <executor>` target. Disabled until ExecuteStmt.Executor exists again
		// (it is currently commented out); nothing calls this yet.
		// private void ResolveExecuteExecutor(ExecuteStmt exec, Scope currentScope)
		// {
		// 	var name = exec.Executor!.Name;
		// 	var found = currentScope.FindVarByName(name);
		// 	if (found is not null)
		// 		exec.Executor = found;
		// 	else
		// 	{
		// 		var range = exec.Executor.Identifier?.Range ?? exec.FullRange ?? exec.InnerRange;
		// 		if (range is not null)
		// 			Errors.Add(new UndefinedSymbolError(range, exec.Executor));
		// 	}
		// }

		private void ProcessImport(ImportStmt import, Scope currentScope)
		{
			var normalizedPath = Path.GetFullPath(import.Filepath);
			if (Modules.TryGetValue(normalizedPath, out var cachedFile))
			{
				import.ImportedScope = cachedFile.ParserResult!.RootScope;
				return;
			}
		}
    }
}
