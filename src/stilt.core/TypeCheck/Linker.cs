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
					if (exec.Executor is not null)
						ProcessExpr(exec.Executor, exec.Scope);

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
				case ArrayLiteralExpr arr:
				{
					if (arr.Value is List<Expr> list)
						foreach (var e in list)
							ProcessExpr(e, currentScope);

					break;
				}
				case TableLiteralExpr tbl:
				{
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

					break;
				}
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

		/// <summary>
		/// Resolves a (possibly qualified, possibly generic) reference path and binds <paramref name="symbol"/> to the
		/// symbol it denotes — the tip of the qualifier chain (e.g. <c>C</c> in <c>a.b.C</c>). On failure an error is
		/// recorded and the reference is left unbound.
		/// </summary>
		private void ResolveReference(SymbolReference symbol, Scope scope)
		{
			var resolved = ResolvePath(symbol.Unresolved, scope);
			if (resolved is not null)
				Bind(symbol, resolved);
		}

		/// <summary>
		/// Resolves the leftmost segment of <paramref name="reference"/> against the lexical <paramref name="scope"/>,
		/// then follows the qualifier chain into member containers. Returns the resolved tip symbol, or null on failure.
		/// </summary>
		private Symbol? ResolvePath(UnresolvedReference reference, Scope scope)
		{
			var name = reference.Name;
			Symbol? found = scope.FindVarByName(name);
			found ??= scope.FindTypeByName(name);

			if (found is null)
			{
				Errors.Add(new UndefinedSymbolError(reference.Token.Range, name));
				return null;
			}

			return ResolveSegment(reference, found, scope);
		}

		/// <summary>
		/// Given the symbol a segment resolved to, validates any generic type arguments on it and, if the segment is
		/// qualified (<c>.rest</c>), walks into the symbol's member container to resolve the remainder of the path.
		/// </summary>
		private Symbol? ResolveSegment(UnresolvedReference reference, Symbol found, Scope scope)
		{
			// Validate generic type arguments (e.g. `int` in `array[int]`). Building the applied type itself is left to
			// generic application; here we only resolve the arguments so undefined ones are reported.
			foreach (var typeArg in reference.TypeArguments)
				ResolveReference(SymbolReference.FromUnresolved(typeArg), scope);

			if (reference.Qualifier is null)
				return found;

			var container = MemberContainerOf(found);
			if (container is null)
			{
				Errors.Add(new UndefinedSymbolError(reference.Qualifier.Token.Range, reference.Qualifier.Name));
				return null;
			}

			return ResolveInContainer(reference.Qualifier, container, scope);
		}

		/// <summary>
		/// Resolves a qualified segment by name within a member <paramref name="container"/>, then continues down the chain.
		/// </summary>
		private Symbol? ResolveInContainer(UnresolvedReference reference, IReadOnlyList<Symbol> container, Scope scope)
		{
			var member = container.FirstOrDefault(s => s.Name == reference.Name);
			if (member is null)
			{
				Errors.Add(new UndefinedSymbolError(reference.Token.Range, reference.Name));
				return null;
			}

			return ResolveSegment(reference, member, scope);
		}

		/// <summary>
		/// The symbols reachable through <c>.</c> on a symbol: an imported module's scope, or a type's members (for a
		/// value, the members of its resolved type). Null when nothing can be accessed off it.
		/// </summary>
		private static IReadOnlyList<Symbol>? MemberContainerOf(Symbol symbol)
		{
            return symbol switch
            {
                VarSymbol v when v.Declaration is ImportStmt { ImportedScope: not null } import => import.ImportedScope.Symbols,
                VarSymbol v => MembersOf(v),
                TypeSymbol t => t.Members,
                _ => null,
            };
        }

		/// <summary>
		/// Resolves a member-access chain (<c>a.b.c</c>), binding each segment's reference: the first segment against
		/// the lexical <paramref name="currentScope"/>, each later segment within the previous segment's member
		/// container. A nested chain segment is resolved first and the walk continues from its tip. Shares the
		/// resolution primitives (<see cref="ResolvePath"/>, <see cref="ResolveInContainer"/>,
		/// <see cref="MemberContainerOf"/>) with <see cref="ResolveReference"/>.
		/// </summary>
		private void ResolveAccessChain(CommaExpr access, Scope currentScope)
		{
			if (access.Exprs.Count == 0) return;

			// Exprs order: [rightmost, ..., leftmost] e.g. a.b.c -> [c, b, a]; walk leftmost-first.
			var exprs = access.Exprs;
			IReadOnlyList<Symbol>? container = null;

			for (int i = exprs.Count - 1; i >= 0; i--)
			{
				switch (exprs[i])
				{
					case CommaExpr nested:
					{
						ResolveAccessChain(nested, currentScope);
						var tip = GetTipIdentityInChain(nested)?.Identity.Resolved;
						if (tip is null)
							return;
						container = MemberContainerOf(tip);
						break;
					}
					case IdentityExpr id:
					{
						// A null container means this is the first segment (resolved against the lexical scope);
						// otherwise the segment is a member of the previous segment's container.
						var resolved = container is null
							? ResolvePath(id.Identity.Unresolved, currentScope)
							: ResolveInContainer(id.Identity.Unresolved, container, currentScope);
						if (resolved is null)
							return;

						Bind(id.Identity, resolved);
						container = MemberContainerOf(resolved);
						break;
					}
				}
			}
		}

		/// <summary>Gets the tip (result) <see cref="IdentityExpr"/> of an access chain — the segment the whole access evaluates to.</summary>
		private static IdentityExpr? GetTipIdentityInChain(CommaExpr comma)
		{
			if (comma.Exprs.Count == 0) return null;
			var first = comma.Exprs[0];
			return first as IdentityExpr ?? (first is CommaExpr c ? GetTipIdentityInChain(c) : null);
		}

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
