using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

		public FileRange? InnerRange { private get; set; }
		public FileRange? Range
		{
			get
			{
				if (this is IOperator op)
				{
					var children = op.GetChildren().Select(c => c?.Range);
					FileRange? sum = null;

					foreach (var child in children.Skip(1))
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
		FileRange? Range { get; }
	}

	public class IdentityExpr : Expr
	{
		public Symbol Identity;
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

		public TernaryExpr(int precedence) { Precedence = precedence; }
		public TernaryExpr(int precedence, FileRange range) { Precedence = precedence; InnerRange = range; }
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

		public BinaryExpr(int precedence) { Precedence = precedence; }
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

		public UnaryExpr(int precedence) { Precedence = precedence; }
	}

	public class IncrementExpr(int p) : UnaryExpr(p) { public bool Prefix = true; }
	public class DecrementExpr(int p) : UnaryExpr(p) { public bool Prefix = true; }
	public class PlusExpr(int p) : UnaryExpr(p) { }
	public class NegationExpr(int p) : UnaryExpr(p) { }
	public class NewExpr(int p) : UnaryExpr(p) { }
	public class CloneExpr(int p) : UnaryExpr(p) { }
	public class LNotExpr(int p) : UnaryExpr(p) { }
	public class BNotExpr(int p) : UnaryExpr(p) { }

	public class AdditionExpr(int p) : BinaryExpr(p) { }
	public class SubtractionExpr(int p) : BinaryExpr(p) { }
	public class DivisionExpr(int p) : BinaryExpr(p) { }
	public class MultiplicationExpr(int p) : BinaryExpr(p) { }
	public class ExponentExpr(int p) : BinaryExpr(p) { }
	public class RangeExpr(int p) : BinaryExpr(p) { }
	public class ModuloExpr(int p) : BinaryExpr(p) { }
	public class LAndExpr(int p) : BinaryExpr(p) { }
	public class LOrExpr(int p) : BinaryExpr(p) { }
	public class LXorExpr(int p) : BinaryExpr(p) { }
	public class BAndExpr(int p) : BinaryExpr(p) { }
	public class BOrExpr(int p) : BinaryExpr(p) { }
	public class BXorExpr(int p) : BinaryExpr(p) { }
	public class BSLExpr(int p) : BinaryExpr(p) { }
	public class BSRExpr(int p) : BinaryExpr(p) { }
	public class GreaterExpr(int p) : BinaryExpr(p) { }
	public class LesserExpr(int p) : BinaryExpr(p) { }
	public class EqualExpr(int p) : BinaryExpr(p) { }
	public class UnequalExpr(int p) : BinaryExpr(p) { }
	public class GreaterOrEqualExpr(int p) : BinaryExpr(p) { }
	public class LesserOrEqualExpr(int p) : BinaryExpr(p) { }
	public class SwapExpr(int p) : BinaryExpr(p) { }
	public class CopyExpr(int p) : BinaryExpr(p) { }
	public class SignalConnectExpr(int p) : BinaryExpr(p) { }
	public class SignalEmitExpr(int p) : BinaryExpr(p) { }
	public class UpdateExpr(int p) : BinaryExpr(p) { }
	public class IndexExpr(int p) : BinaryExpr(p) { }
	public class AccessExpr(int p) : BinaryExpr(p) { }
	public class SelfAccessExpr(int p) : BinaryExpr(p) { }
	public class CommaExpr(int p) : BinaryExpr(p) { }

	public class ConditionalExpr(int p) : TernaryExpr(p) { }

	public class AssignExpr(int p) : BinaryExpr(p)
	{
		//	   TokenType?
		public BinaryExpr? Operation;
	}

	public class PrototypeLiteralExpr : Expr
	{
		public List<Expr> Array = [];
	}

	public class CallExpr(int p) : BinaryExpr(p) { }

	public class LiteralExpr : Expr
	{
		public required object Value;
	}

	public class LambdaFuncExpr : Expr
	{
		public List<VarSymbol> Arguments = new();
		public Stmt Value;
	}
}
