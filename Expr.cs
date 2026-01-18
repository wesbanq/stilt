using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace stilt.AST
{
	public abstract class Expr 
	{
		public bool Bracketed = false;

		public List<Expr> PostorderWalk(Predicate<Expr>? predicate = null, List<Expr>? sofar = null)
		{
			sofar ??= [];
			predicate ??= (e => !e.Bracketed);
			
			if (predicate.Invoke(this))
			{
				switch (this)
				{
					case TertiaryExpr t:
					{
						sofar.AddRange(t.Left?.PostorderWalk(predicate, sofar));
						sofar.AddRange(t.Middle?.PostorderWalk(predicate, sofar));
						sofar.AddRange(t.Right?.PostorderWalk(predicate, sofar));
						break;
					}
					case BinaryExpr b:
					{
						sofar.AddRange(b.Left?.PostorderWalk(predicate, sofar));
						sofar.AddRange(b.Right?.PostorderWalk(predicate, sofar));
						break;
					}
					case UnaryExpr u:
					{
						sofar.AddRange(u.Leaf?.PostorderWalk(predicate, sofar));
						break;
					}
					default:
					{
						throw new Exception("very bad things just happened");
					}
				}
			}
			sofar.Add(this);
			return sofar;
		}

		public Expr? FindFirstNull()
		{
			return FindFirst(e =>
			{
				return e switch
				{
					TertiaryExpr t => t.Right == null || t.Middle == null || t.Left == null,
					BinaryExpr b => b.Right == null || b.Left == null,
					UnaryExpr u => u.Leaf == null,
					_ => throw new Exception("something bad just happened"),
				};
			});
		}

		public Expr? FindFirstPrecedence(int precedence)
		{
			return FindFirst(e =>
			{
				return true;
			});
		}

		public Expr? FindFirst(Predicate<Expr> predicate)
		{
			if (predicate.Invoke(this))
				return this;

			if (!this.Bracketed)
				return null;

			switch (this)
			{
				case TertiaryExpr t:
					return (t.Right?.FindFirst(predicate) ?? t.Middle?.FindFirst(predicate)) ?? t.Left?.FindFirst(predicate);
				case BinaryExpr b:
					return b.Right?.FindFirst(predicate) ?? b.Left?.FindFirst(predicate);
				case UnaryExpr u:
					return u.Leaf?.FindFirst(predicate);
				default:
					throw new Exception("very bad things just happened");
			}
		}
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

	public abstract class TertiaryExpr : Expr
	{
		public Expr? Left;
		public Expr? Middle;
		public Expr? Right;
	}

	public abstract class BinaryExpr : Expr
	{
		public Expr? Left;
		public Expr? Right;
	}

	public abstract class UnaryExpr : Expr
	{
		public Expr? Leaf;
	}

	public class IncrementExpr : UnaryExpr { }
	public class DecrementExpr : UnaryExpr { }
	public class NegationExpr : UnaryExpr { }
	public class AdditionExpr : BinaryExpr { }
	public class SubtractionExpr : BinaryExpr { }
	public class DivisionExpr : BinaryExpr { }
	public class MultiplicationExpr : BinaryExpr { }
	public class ExponentExpr : BinaryExpr { }
	public class RangeExpr : BinaryExpr { }
	public class ModuloExpr : BinaryExpr { }
	public class LAndExpr : BinaryExpr { }
	public class LOrExpr : BinaryExpr { }
	public class LXorExpr : BinaryExpr { }
	public class LNotExpr : BinaryExpr { }
	public class BAndExpr : BinaryExpr { }
	public class BOrExpr : BinaryExpr { }
	public class BXorExpr : BinaryExpr { }
	public class BNotExpr : BinaryExpr { }
	public class BSLExpr : BinaryExpr { }
	public class BSRExpr : BinaryExpr { }
	public class GreaterExpr : BinaryExpr { }
	public class LesserExpr : BinaryExpr { }
	public class EqualExpr : BinaryExpr { }
	public class UnequalExpr : BinaryExpr { }
	public class GreaterOrEqualExpr : BinaryExpr { }
	public class LesserOrEqualExpr : BinaryExpr { }
	public class SwapExpr : BinaryExpr { }
	public class CopyExpr : BinaryExpr { }
	public class SignalConnectExpr : BinaryExpr { }
	public class SignalEmitExpr : BinaryExpr { }
	public class UpdateExpr : BinaryExpr { }
	public class IndexExpr : BinaryExpr { }
	//left should be identityexpr
	public class AssignExpr : BinaryExpr 
	{
		public Expr? Operation;
	}

	public class ArrayExpr : Expr
	{
		public List<Expr> Array = new List<Expr>();
	}

	public class CallExpr : Expr
	{
		[Required] public FuncSymbol Function;
		public List<Expr> Arguments = new();
	}

	public class LiteralExpr : Expr
	{
		[Required] public object Value;
	}


}
