using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

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

		public Expr? FindFirstPrecedenceOrNull(int precedence, out Expr? parent)
		{
			//first find any null children and only then check 4 precedence
			parent = null;
			FindFirstNull(out parent);
			if (parent != null)
				return null;
			else 
				return FindFirstPrecedence(precedence, out parent);
		}

		protected Expr? FindFirstNull(out Expr? parent)
		{
			parent = null;
			var firstNull = FindFirst(e =>
			{
				if (e is IOperator spreadable)
				{
					return spreadable.GetChildren().Any(c => c == null);
				}
				return false;
			}, out parent);
			if (firstNull != null)
			{
				parent = firstNull;
				return null;
			}
			parent = null;
			return null;
		}

		protected Expr? FindFirstPrecedence(int precedence, out Expr? parent)
		{
			parent = null;
			return FindFirst(e =>
			{
				return e.Precedence <= precedence;
			}, out parent);
		}

		protected Expr? FindFirst(Predicate<Expr> predicate, out Expr? parent, Expr? supposedParent = null)
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
					if (res != null)
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
		FileRange? InnerRange { set; }
		FileRange? FullRange { get; }
	}

	public class IdentityExpr : Expr
	{
		public Symbol Identity;
	}

	public abstract class UnaryExpr : Expr, IOperator
	{
		public Expr? Leaf;

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

			//change error
			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}
		public void InsertChild(Expr what)
		{
			if (Leaf == null)
			{
				Leaf = what;
				return;
			}
			//change error
			throw new ArgumentException();
		}

		public UnaryExpr(int precedence, FileRange? range = null) { Precedence = precedence; InnerRange = range; }
	}

	public abstract class BinaryExpr : Expr, IOperator
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

			//change error
			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}
		public void InsertChild(Expr what)
		{
			if (Left == null)
			{
				Left = what;
				return;
			}
			if (Right == null)
			{
				Right = what;
				return;
			}
			
			//change error
			throw new ArgumentException();
		}

		public BinaryExpr(int precedence, FileRange? range = null) { Precedence = precedence; InnerRange = range; }
	}

	public abstract class TernaryExpr : Expr, IOperator
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

			//change error
			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}

		public void InsertChild(Expr what)
		{
			if (Left == null)
			{
				Left = what;
				return;
			}
			if (Middle == null)
			{
				Middle = what;
				return;
			}
			if (Right == null)
			{
				Right = what;
				return;
			}

			//change error
			throw new ArgumentException();
		}

		public TernaryExpr(int precedence, FileRange? range = null) { Precedence = precedence; InnerRange = range; }
	}

	public class CommaExpr : Expr, IOperator
	{
		public List<Expr> Exprs = [];
		public int ExprLength = 2;

		public Expr[]? GetChildren()
		{
			return ExprLength < Exprs.Count ? [null, .. Exprs] : [.. Exprs];
		}

		public void InsertChild(Expr what)
		{
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

		public CommaExpr(int precedence, FileRange? range = null) { Precedence = precedence; InnerRange = range; }
	}

	public class PlusExpr(int p, FileRange r) : UnaryExpr(p, r) { }
	public class NegationExpr(int p, FileRange r) : UnaryExpr(p, r) { }
	public class IncrementExpr(int p, FileRange r) : UnaryExpr(p, r) { public bool Prefix = true; }
	public class DecrementExpr(int p, FileRange r) : UnaryExpr(p, r) { public bool Prefix = true; }
	public class NewExpr(int p, FileRange r) : UnaryExpr(p, r) { }
	public class CloneExpr(int p, FileRange r) : UnaryExpr(p, r) { }
	public class LNotExpr(int p, FileRange r) : UnaryExpr(p, r) { }
	public class BNotExpr(int p, FileRange r) : UnaryExpr(p, r) { }

	public class AdditionExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class SubtractionExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class DivisionExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class MultiplicationExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class ExponentExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class RangeExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class ModuloExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class LAndExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class LOrExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class LXorExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class BAndExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class BOrExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class BXorExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class BSLExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class BSRExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class GreaterExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class LesserExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class EqualityExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class InequalityExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class GreaterOrEqualExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class LesserOrEqualExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class SwapExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class CopyExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class OverwriteExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class CompositionExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class SignalConnectExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class SignalEmitExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class UpdateExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class IndexExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class AccessExpr(int p, FileRange r) : BinaryExpr(p, r) { }
	public class SelfAccessExpr(int p, FileRange r) : BinaryExpr(p, r) { }

	public class ConditionalExpr(int p, FileRange r) : TernaryExpr(p, r) { }

	public class AssignExpr(int p, FileRange r) : BinaryExpr(p, r)
	{
		public TokenType? Operation;
	}

	public class CallExpr(int p, FileRange r) : BinaryExpr(p, r) { }

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
		[SetsRequiredMembers]
		public StringLiteralExpr(string str, FileRange? r)
			: base(r)
		{
			Value = str;
			Type = Builtins.String;
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
