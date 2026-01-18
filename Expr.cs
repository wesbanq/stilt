using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;

namespace stilt.AST
{
	public abstract class Expr
	{
		public bool Bracketed = false;
		public int Precedence = 0;

		public Expr? FindFirstNull(out Expr? parent)
		{
			parent = null;
			return FindFirst(e => 
			{
				if (e is ISpreadable walkable)
				{
					return walkable.Spread().Any(c => c == null);
				}
				return false;
				//return e == null;
			}, out parent);
		}

		public Expr? FindFirstPrecedence(int precedence, out Expr? parent)
		{
			parent = null;
			return FindFirst(e =>
			{
				return e.Precedence <= precedence;
			}, out parent);
		}

		public Expr? FindFirst(Predicate<Expr> predicate, out Expr? parent, Expr? supposedParent = null)
		{
			parent = supposedParent;
			if (predicate.Invoke(this))
			{
				return this;
			}

			if (supposedParent != null && Bracketed)
			{
				return null;
			}

			if (this is ISpreadable walkable)
			{
				foreach (var child in walkable.Spread())
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

	public interface ISpreadable
	{
		Expr?[] Spread();
		void Replace(Expr what, Expr with);
	}

	public class IdentitiyExpr : Expr 
	{
		public required Symbol Identity;
	}

	public class AccessExpr : IdentitiyExpr
	{
		public IdentitiyExpr? From;
	}

	public abstract class Symbol 
	{
		[Required] public string Name;
		[Required] public string Source;

		public static bool operator ==(Symbol left, Symbol right)
		{
			return left.Name.Equals(right.Name) && left.Source.Equals(right.Source);
		}

		public override bool Equals(object other)
		{
			return other is Symbol && this == other;
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public static bool operator !=(Symbol left, Symbol right)
		{
			return !(left == right);
		}

		public Symbol(string name, string src)
		{
			Name = name;
			Source = src;
		}
	}

	public class VarSymbol(string n, string s) : Symbol(n, s) { }
	public class FuncSymbol(string n, string s) : Symbol(n, s) { }
	public class TypeSymbol(string n, string s) : Symbol(n, s) { }

	public abstract class TernaryExpr : Expr, ISpreadable
	{
		public Expr? Left;
		public Expr? Middle;
		public Expr? Right;

		public Expr?[] Spread()
		{
			return [Right, Middle, Left];
		}
		public void Replace(Expr what, Expr with)
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

			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}

		public TernaryExpr(Expr? left, Expr? middle, Expr? right) { Left = left; Middle = middle; Right = right; }
	}

	public abstract class BinaryExpr : Expr, ISpreadable
	{
		public Expr? Left;
		public Expr? Right;

		public Expr?[] Spread()
		{
			return [Right, Left];
		}
		public void Replace(Expr what, Expr with)
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

			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}

		public BinaryExpr(Expr? left, Expr? right) { Left = left; Right = right; }
	}

	public abstract class UnaryExpr : Expr
	{
		public Expr? Leaf;

		public Expr?[] Spread()
		{
			return [Leaf];
		}
		public void Replace(Expr what, Expr with)
		{
			if (ReferenceEquals(Leaf, what))
			{
				Leaf = with;
				return;
			}

			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}

		public UnaryExpr(Expr? leaf) { Leaf = leaf; }
	}

	public class IncrementExpr(Expr? l) : UnaryExpr(l) { }
	public class DecrementExpr(Expr? l) : UnaryExpr(l) { }
	public class NegationExpr(Expr? l) : UnaryExpr(l) { }
	public class AdditionExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class SubtractionExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class DivisionExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class MultiplicationExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class ExponentExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class RangeExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class ModuloExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LAndExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LOrExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LXorExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LNotExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BAndExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BOrExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BXorExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BNotExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BSLExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class BSRExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class GreaterExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LesserExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class EqualExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class UnequalExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class GreaterOrEqualExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class LesserOrEqualExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class SwapExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class CopyExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class SignalConnectExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class SignalEmitExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class UpdateExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }
	public class IndexExpr(Expr? l, Expr? r) : BinaryExpr(l, r) { }

	public class AssignExpr(IdentitiyExpr? l, Expr? r) : BinaryExpr(l, r) 
	{
		public Expr? Operation;
	}
	public class ArrayExpr : Expr
	{
		public List<Expr> Array = new List<Expr>();
	}

	public class CallExpr(IdentitiyExpr? l, ArrayExpr? r) : BinaryExpr(l, r) { }

	public class LiteralExpr : Expr
	{
		[Required] public object Value;
	}


}
