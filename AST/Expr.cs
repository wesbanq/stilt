using System.Diagnostics.CodeAnalysis;

namespace stilt.AST
{
	public abstract class Expr : IRanged
	{
		public TypeSymbol Type = Builtins.Any;
		public bool Bracketed = false;
		public bool Explicit = false;
		public int Precedence = 0;

		public FileRange? InnerRange { get; set; }
		public FileRange? FullRange
		{
			get
			{
				if (this is IOperator op)
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
				if (e is IOperator spreadable)
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

			if (this is IOperator spreadable)
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
		
		//public Expr(int precedence, FileRange range) { Precedence = precedence; InnerRange = range; }
		//public Expr(int precedence) { Precedence = precedence; }
	}

	public interface IOperator
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

	public class IdentityExpr : Expr
	{
		public Symbol Identity;
	}

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

	public class UnaryExpr : OperationExpr, IOperator
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

	public class BinaryExpr : OperationExpr, IOperator
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

	public class TernaryExpr : OperationExpr, IOperator
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

	public class CommaExpr : OperationExpr, IOperator
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

	public class AccessExpr : CommaExpr
	{
		public AccessExpr(int p, FileRange? r = null, Token? o = null)
			: base(p, r, o)
		{ }
	}
	
	public class NullAccessExpr : CommaExpr
	{
		public NullAccessExpr(int p, FileRange? r = null, Token? o = null)
			: base(p, r, o)
		{ }
	}

	public class AssignExpr : BinaryExpr
	{
		public TokenType? Operation;

		public AssignExpr(int p, FileRange? r, Token? o = null) 
			: base(p, r, o)
		{ }
	}

	public class CallExpr : BinaryExpr
	{
		public CallExpr(int p, FileRange? r, Token? o = null) 
			: base(p, r, o)
		{ }
	}

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

	public class LambdaFuncExpr : Expr
	{
		public List<VarSymbol> Arguments = [];
		public Stmt Value;
	}
}
