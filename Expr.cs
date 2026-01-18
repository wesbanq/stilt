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

		public Expr? FindFirstPrecedenceOrNull(int precedence, out Expr? parent)
		{
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
				if (e is ISpreadable spreadable)
				{
					return spreadable.Spread().Any(c => c == null);
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
			parent = supposedParent;

			if (predicate.Invoke(this))
			{
				return this;
			}

			if (Bracketed)
			{
				return null;
			}

			if (this is ISpreadable spreadable)
			{
				foreach (var child in spreadable.Spread())
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
		void Replace(Expr? what, Expr with);
		void Shove(Expr what);
	}

	public class IdentitiyExpr : Expr
	{
		public required Symbol Identity;
	}
	//public class AccessExpr : IdentitiyExpr
	//{
	//	public IdentitiyExpr? From;
	//}

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
		public void Replace(Expr? what, Expr with)
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

		public void Shove(Expr what)
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

			throw new ArgumentException();
		}

		public TernaryExpr(int precedence) { Precedence = precedence; }
	}

	public abstract class BinaryExpr : Expr, ISpreadable
	{
		public Expr? Left;
		public Expr? Right;

		public Expr?[] Spread()
		{
			return [Right, Left];
		}
		public void Replace(Expr? what, Expr with)
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
		public void Shove(Expr what)
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
			
			throw new ArgumentException();
		}

		public BinaryExpr(int precedence) { Precedence = precedence; }
	}

	public abstract class UnaryExpr : Expr, ISpreadable
	{
		public Expr? Leaf;

		public Expr?[] Spread()
		{
			return [Leaf];
		}
		public void Replace(Expr? what, Expr with)
		{
			if (ReferenceEquals(Leaf, what))
			{
				Leaf = with;
				return;
			}

			throw new ArgumentException("The node to replace was not found in the children.", nameof(what));
		}
		public void Shove(Expr what)
		{
			if (Leaf == null)
			{
				Leaf = what;
				return;
			}
			throw new ArgumentException();
		}

		public UnaryExpr(int precedence) { Precedence = precedence; }
	}

	public class IncrementExpr(int p) : UnaryExpr(p) { }
	public class DecrementExpr(int p) : UnaryExpr(p) { }
	public class NegationExpr(int p) : UnaryExpr(p) { }
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
	public class LNotExpr(int p) : BinaryExpr(p) { }
	public class BAndExpr(int p) : BinaryExpr(p) { }
	public class BOrExpr(int p) : BinaryExpr(p) { }
	public class BXorExpr(int p) : BinaryExpr(p) { }
	public class BNotExpr(int p) : BinaryExpr(p) { }
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

	public class AssignExpr(int p) : BinaryExpr(p)
	{
		public BinaryExpr? Operation;
	}

	public class ConditionalExpr(int p) : TernaryExpr(p) { }

	public class ArrayExpr : Expr
	{
		public List<Expr> Array = [];
	}

	public class CallExpr(int p) : BinaryExpr(p) { }

	public class LiteralExpr : Expr
	{
		[Required] public object Value;
	}


}
