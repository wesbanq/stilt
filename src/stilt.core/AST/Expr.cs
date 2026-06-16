using System.Diagnostics.CodeAnalysis;

namespace stilt.AST
{
	/// <summary>
	/// Base of every expression node. Expressions form a tree the parser grows one token at a time via
	/// <see cref="InsertIntoTree(Expr?)"/>, which places each node according to its <see cref="Precedence"/>
	/// (lower binds tighter) and <see cref="Bracketed"/> flag. Operator nodes implement <see cref="ITraversible"/>
	/// to expose and rewire their children, which is what makes that precedence-based tree surgery possible.
	/// <see cref="Type"/> starts as <see cref="Builtins.None"/> and is meant to be filled in by type checking;
	/// <see cref="InnerRange"/>/<see cref="FullRange"/> track the node's own span and its whole subtree's span.
	/// </summary>
	public abstract class Expr : IRanged//, ITraversible
	{
        // public abstract Expr?[] GetChildren();
		// public abstract void ReplaceChild(Expr expr, Expr expr1);
		// public abstract void InsertChild(Expr expr);

		/// <summary>
		/// Inserts <paramref name="newExpr"/> into the tree rooted at <paramref name="rootExpr"/>.
		/// When <paramref name="rootExpr"/> is non-null, it is treated as the current root (same as calling <see cref="InsertIntoTree"/> on it).
		/// </summary>
		public static Expr? InsertIntoTree(Expr? rootExpr, Expr? newExpr)
		{
			if (rootExpr is null && newExpr is not null)
			{
				if (!newExpr.Bracketed && newExpr is not UnaryExpr && newExpr is not CommaExpr && newExpr is ITraversible)
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
				return newExpr;
			}
			if (newExpr is null)
				return rootExpr;
			if (rootExpr is null)
				return null;
			return rootExpr.InsertIntoTree(newExpr);
		}

		/// <summary>
		/// Inserts <paramref name="newExpr"/> into this expression tree (<c>this</c> is the root).
		/// </summary>
		/// <returns>The root of the tree after insertion (may differ from <c>this</c>).</returns>
		public Expr? InsertIntoTree(Expr? newExpr)
		{
			if (newExpr is null)
				return this;

			Expr? rootExpr = this;
			var toReplace = rootExpr.FindFirstPrecedenceOrNull(newExpr.Precedence, out var parent);
			if (toReplace is null && parent is null)
			{
				if (newExpr is ITraversible exprSpreadable)
				{
					exprSpreadable.InsertChild(rootExpr);
					rootExpr = newExpr;
				}
				else
					throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
			}

			if (newExpr is ITraversible spreadable)
			{
				if (toReplace is not null)
				{
					if (toReplace is CommaExpr rootComma && newExpr is CommaExpr)
					{
						++rootComma.ExprLength;
						return rootExpr;
					}

					spreadable.InsertChild(toReplace);
					if (parent is not null)
					{
						if (parent is ITraversible op)
							op.ReplaceChild(toReplace, newExpr);
						else
							throw new MalformedExpr((toReplace ?? newExpr)!.GetFullRangeOrThrow());
					}
					else
						rootExpr = newExpr;

					return rootExpr;
				}

				if (parent is not null)
				{
					if (parent is ITraversible sParent)
					{
						if (toReplace is null && (newExpr.Bracketed || newExpr is (UnaryExpr or TernaryExpr)))
							sParent.InsertChild(newExpr);
						else
							throw new MalformedExpr(newExpr.GetFullRangeOrThrow());
					}
					else
						throw new MalformedExpr(newExpr.GetFullRangeOrThrow());

					return rootExpr;
				}

				rootExpr = newExpr;
				return rootExpr;
			}

			if (parent is ITraversible newSpreadable)
				newSpreadable.InsertChild(newExpr);
			else
				throw new MalformedExpr(newExpr.GetFullRangeOrThrow());

			return rootExpr;
		}

		public TypeSymbol Type = Builtins.None;
		public bool Bracketed = false;
		public int Precedence = 0;

		public FileRange? InnerRange { get; set; }
		public FileRange? FullRange
		{
			get
			{
				if (this is ITraversible op)
				{
					var children = op.GetChildren().Select(c => c?.FullRange);
					FileRange? sum = InnerRange;

					foreach (var child in children)
					{
						if (child is null) continue;
						sum = sum is null ? child : sum + child;
					}
					return sum;
				}
				
				return InnerRange;
			}
		}

		public FileRange GetFullRangeOrThrow() =>
			FullRange ?? throw new InvalidOperationException("Expression has no FullRange");

		public FileRange GetInnerRangeOrFullRangeOrThrow() =>
			InnerRange ?? FullRange ?? throw new InvalidOperationException("Expression has no range");

		public Expr? FindFirstPrecedenceOrNull(int precedence, out Expr? parent)
		{
			//first find any null children and only then check 4 precedence
			parent = null;
			FindFirstNull(out parent);
			if (parent is not null)
				return null;
			else 
				return FindFirstPrecedence(precedence, out parent);
		}

		public Expr? FindFirstNull(out Expr? parent)
		{
			parent = null;
			var firstNull = FindFirst(e =>
			{
				if (e is ITraversible spreadable)
				{
					return !e.Bracketed && spreadable.GetChildren().Any(c => c is null);
				}
				return false;
			}, out parent);
			if (firstNull is not null)
			{
				parent = firstNull;
				return null;
			}
			parent = null;
			return null;
		}

		public Expr? FindFirstPrecedence(int precedence, out Expr? parent)
		{
			parent = null;
			return FindFirst(e =>
			{
				return e.Bracketed || e.Precedence <= precedence;
			}, out parent);
		}

		public Expr? FindFirst(Predicate<Expr> predicate, out Expr? parent, Expr? supposedParent = null)
		{
			//reverse preorder walk
			parent = supposedParent;

			if (predicate.Invoke(this))
			{
				return this;
			}

			//check root of bracketed expr but not its children
			if (Bracketed)
			{
				return null;
			}

			if (this is ITraversible spreadable)
			{
				foreach (var child in spreadable.GetChildren())
				{
					var res = child?.FindFirst(predicate, out parent, this);
					if (res is not null)
					{
						return res;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Prefer the most-recently-added leaf node in the current tree.
		/// Child ordering in <see cref="ITraversible.GetChildren"/> is already "newest-first" for operators.
		/// </summary>
		public Expr GetLastExprInTree()
		{
			var current = this;
			while (!current.Bracketed && current is ITraversible op)
			{
				var next = op.GetChildren().First(c => c is not null);
				if (next is null)
					break;
				current = next;
			}
			return current;
		}

		/// <summary>
		/// Whether the next token should be treated as an operator (vs an operand), based on <see cref="FindFirstNull"/>.
		/// </summary>
		public static bool ExpectingOperator(Expr? expr)
		{
			if (expr is null)
				return false;
			var a = expr.FindFirstNull(out var p);
			return a is null && p is null;
		}
	}

	/// <summary>
	/// Implemented by operator nodes that hold child expressions, giving <see cref="Expr.InsertIntoTree(Expr?)"/> a
	/// uniform way to read and rewire them. <see cref="GetChildren"/> returns children newest-first (operands are
	/// inserted in reverse), which is what lets the tree builder find the most recent insertion point.
	/// </summary>
	public interface ITraversible
	{
		Expr?[] GetChildren();
		void ReplaceChild(Expr what, Expr with);
		void InsertChild(Expr what);
	}

	public interface IRanged
	{
		[JsonIgnore]
		FileRange? InnerRange { set; }
		[JsonIgnore]
		FileRange? FullRange { get; }
		FileRange GetFullRangeOrThrow();
		FileRange GetInnerRangeOrFullRangeOrThrow();
	}

	/// <summary>A bare name use. Holds a <see cref="SymbolReference"/> that is unresolved until the <see cref="Linker"/> binds it to a symbol.</summary>
	public class IdentityExpr : Expr
	{
		public SymbolReference Identity;
	}

	/// <summary>Base for operator nodes (unary/binary/ternary/comma). Carries the source <see cref="Operator"/> token and a precedence.</summary>
	public abstract class OperationExpr : Expr
	{
		public Token? Operator;

		protected OperationExpr(int precedence, FileRange? range = null, Token? op = null)
		{
			Precedence = precedence;
			InnerRange = range;
			Operator = op;
		}
	}

	public class UnaryExpr : OperationExpr, ITraversible
	{
		public Expr? Leaf;
		public bool Prefix = true; // For increment/decrement operators

		public Expr?[] GetChildren()
		{
			return [Leaf];
		}
		public void ReplaceChild(Expr what, Expr with)
		{
			if (ReferenceEquals(Leaf, what))
			{
				Leaf = with;
				return;
			}

			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to replace a child node that does not exist in the expression tree.");
			throw new SyntaxError(range, "Internal error: Attempted to replace a child node that does not exist in the expression tree.");
		}
		public void InsertChild(Expr what)
		{
			if (Leaf is null)
			{
				Leaf = what;
				return;
			}
			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to insert a child into an expression node that is already full.");
			throw new SyntaxError(range, "Internal error: Attempted to insert a child into an expression node that is already full.");
		}

		public UnaryExpr(int p, FileRange? r = null, Token? o = null) 
			: base(p, r, o)
		{ }
	}

	public class BinaryExpr : OperationExpr, ITraversible
	{
		public Expr? Left;
		public Expr? Right;

		public Expr?[] GetChildren()
		{
			return [Right, Left];
		}
		public void ReplaceChild(Expr what, Expr with)
		{
			if (ReferenceEquals(Right, what))
			{
				Right = with;
				return;
			}
			if (ReferenceEquals(Left, what))
			{
				Left = with;
				return;
			}

			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to replace a child node that does not exist in the expression tree.");
			throw new SyntaxError(range, "Internal error: Attempted to replace a child node that does not exist in the expression tree.");
		}
		public void InsertChild(Expr what)
		{
			if (Left is null)
			{
				Left = what;
				return;
			}
			if (Right is null)
			{
				Right = what;
				return;
			}
			
			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to insert a child into an expression node that is already full.");
			throw new SyntaxError(range, "Internal error: Attempted to insert a child into an expression node that is already full.");
		}

		public BinaryExpr(int p, FileRange? r = null, Token? o = null) 
			: base(p, r, o)
		{ }
	}

	public class TernaryExpr : OperationExpr, ITraversible
	{
		public Expr? Left;
		public Expr? Middle;
		public Expr? Right;

		public Expr?[] GetChildren()
		{
			return [Right, Middle, Left];
		}
		public void ReplaceChild(Expr what, Expr with)
		{
			if (ReferenceEquals(Right, what))
			{
				Right = with;
				return;
			}
			if (ReferenceEquals(Middle, what))
			{
				Middle = with;
				return;
			}
			if (ReferenceEquals(Left, what))
			{
				Left = with;
				return;
			}

			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to replace a child node that does not exist in the expression tree.");
			throw new SyntaxError(range, "Internal error: Attempted to replace a child node that does not exist in the expression tree.");
		}

		public void InsertChild(Expr what)
		{
			if (Left is null)
			{
				Left = what;
				return;
			}
			if (Middle is null)
			{
				Middle = what;
				return;
			}
			if (Right is null)
			{
				Right = what;
				return;
			}

			var range = what?.FullRange ?? this.FullRange ?? InnerRange;
			if (range is null)
				throw new InvalidOperationException("Internal error: Attempted to insert a child into an expression node that is already full.");
			throw new SyntaxError(range, "Internal error: Attempted to insert a child into an expression node that is already full.");
		}

		public TernaryExpr(int p, FileRange? r = null, Token? o = null) 
			: base(p, r, o)
		{ }
	}

	/// <summary>A variadic operator node holding a list of sub-expressions — comma-separated lists, and (via its <see cref="AccessExpr"/>/<see cref="NullAccessExpr"/> subclasses) member-access chains. <see cref="ExprLength"/> is the expected arity while the node is still being filled.</summary>
	public class CommaExpr : OperationExpr, ITraversible
	{
		public List<Expr> Exprs = [];
		public int ExprLength = 2;

		public Expr?[] GetChildren()
		{
			return ExprLength > Exprs.Count ? [null, .. Exprs] : [.. Exprs];
		}

		public void InsertChild(Expr what)
		{
			// var children = GetChildren();
			// if (children.Length == 0 || children[0] is null)
			// 	throw new InvalidOperationException("Internal error: Attempted to insert a child into an expression node that is already full.");
			// children[0] = what;
			// Exprs = [.. children];
			Exprs = [.. Exprs.Prepend(what)];
		}

		public void ReplaceChild(Expr what, Expr with)
		{
			var i = Exprs.FindIndex(e => ReferenceEquals(e, what));
			if (i != -1)
				Exprs[i] = with;
			else
				throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}

		public CommaExpr(int p, FileRange? r = null, Token? o = null) 
			: base(p, r, o)
		{ }
	}

	/// <summary>Member access <c>a.b.c</c>; stored as a chain whose segments the <see cref="Linker"/> resolves left along the dots.</summary>
	public class AccessExpr : CommaExpr
	{
		public AccessExpr(int p, FileRange? r = null, Token? o = null)
			: base(p, r, o)
		{ }
	}

	/// <summary>Null-safe member access <c>a?.b</c>; like <see cref="AccessExpr"/> but short-circuits on a null receiver.</summary>
	public class NullAccessExpr : CommaExpr
	{
		public NullAccessExpr(int p, FileRange? r = null, Token? o = null)
			: base(p, r, o)
		{ }
	}

	/// <summary>An assignment <c>a = b</c>. <see cref="Operation"/> is the underlying operator for a compound form (e.g. <c>+=</c> carries <c>+</c>), or plain <c>=</c>.</summary>
	public class AssignExpr : BinaryExpr
	{
		public TokenType? Operation;

		public AssignExpr(int p, FileRange? r, Token? o = null)
			: base(p, r, o)
		{ }
	}

	/// <summary>A function call: <see cref="BinaryExpr.Left"/> is the callee, <see cref="BinaryExpr.Right"/> the argument list (a <see cref="CommaExpr"/>).</summary>
	public class CallExpr : BinaryExpr
	{
		public CallExpr(int p, FileRange? r, Token? o = null)
			: base(p, r, o)
		{ }
	}

	/// <summary>Base for constant-value nodes (numbers, strings, bools, null, array/table literals); <see cref="Value"/> holds the parsed value and <see cref="Expr.Type"/> its builtin type.</summary>
	public class LiteralExpr : Expr
	{
		public required object? Value;
		public LiteralExpr(FileRange? range) { InnerRange = range; }
	}

	public class NullLiteralExpr : LiteralExpr
	{
		[SetsRequiredMembers]
		public NullLiteralExpr(FileRange? r)
			: base(r)
		{
			Value = null;
			Type = Builtins.None;
		}
	}

	public class BoolLiteralExpr : LiteralExpr
	{
		[SetsRequiredMembers]
		public BoolLiteralExpr(bool value, FileRange? r)
			: base(r)
		{
			Value = value;
			Type = Builtins.Bool;
		}
	}

	public class NumLiteralExpr : LiteralExpr
	{
		[SetsRequiredMembers]
		public NumLiteralExpr(long num, FileRange? r, TypeSymbol? t = null)
			: base(r)
		{
			Value = num;
			Type = t ?? Builtins.Num;
		}
		[SetsRequiredMembers]
		public NumLiteralExpr(double num, FileRange? r, TypeSymbol? t = null)
			: base(r)
		{
			Value = num;
			Type = t ?? Builtins.Num;
		}
	}

	public class StringLiteralExpr : LiteralExpr
	{
		public bool Format = false;
		public bool Tagged = false;
		public bool Multi = false;
		public bool Raw = false;

		[SetsRequiredMembers]
		public StringLiteralExpr(string str, FileRange? r, bool format = false, bool tagged = false, bool multi = false, bool raw = false)
			: base(r)
		{
			Value = str;
			Format = format;
			Tagged = tagged;
			Multi = multi;
			Raw = raw;
			Type = tagged ? Builtins.TaggedString : Builtins.String;
		}
	}

	public class ArrayLiteralExpr : LiteralExpr
	{
		[SetsRequiredMembers]
		public ArrayLiteralExpr(FileRange? r)
			: base(r)
		{
			Type = Builtins.Array;
		}
		[SetsRequiredMembers]
		public ArrayLiteralExpr(FileRange? r, List<Expr> exprs)
			: base(r)
		{
			Value = exprs;
			Type = Builtins.Array;
		}
	}

	public class TableLiteralExpr : LiteralExpr
	{
		[SetsRequiredMembers]
		public TableLiteralExpr(FileRange? r)
			: base(r)
		{
			Type = Builtins.Table;
		}
		[SetsRequiredMembers]
		public TableLiteralExpr(FileRange? r, Dictionary<Symbol, Expr> exprs)
			: base(r)
		{
			Value = exprs;
			Type = Builtins.Table;
		}
	}

	/// <summary>An anonymous function (lambda): its <see cref="Arguments"/> and body <see cref="Value"/>. Parsed and linked, though IR lowering for it is not implemented yet.</summary>
	public class FuncLiteralExpr : Expr
	{
		public IEnumerable<VarSymbol> Arguments;
		public Stmt Value;
	}
}
