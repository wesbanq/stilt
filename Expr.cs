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
						throw new Exception("This class doesn't support walking. (cripple ahh)");
					}
				}
			}
			sofar.Add(this);
			return sofar;
		}
	}
	
	public class IdentitiyExpr : Expr 
	{
		[Required]
		public Symbol Identity;
	}

	public class AccessExpr : IdentitiyExpr
	{
		public IdentitiyExpr? From;
	}

	public abstract class Symbol 
	{
		[Required]
		public string Name;
		[Required]
		public string Source;

		public static bool operator ==(Symbol left, Symbol right)
		{
			return left.Name.Equals(right.Name) && left.Source.Equals(right.Source);
		}

		public override bool Equals(object other)
		{
			return other is Symbol ? this == other : false;
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
		//public Token Operation;
	}

	public abstract class UnaryExpr : Expr
	{
		public Expr? Leaf;
		//public Token Operation;
	}

	//public class BracketedExpr : UnaryExpr { }

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
	public class AssignExpr : BinaryExpr { }
	public class SwapExpr : BinaryExpr { }
	public class CopyExpr : BinaryExpr { }
	public class SignalConnectExpr : BinaryExpr { }
	public class SignalEmitExpr : BinaryExpr { }
	public class UpdateExpr : BinaryExpr { }

	public class ArrayExpr : Expr
	{
		public List<Expr> Array = new List<Expr>();
	}

	public class CallExpr : Expr
	{
		[Required]
		public FuncSymbol Function;
		public List<Expr> Arguments = new();
	}

	public class LiteralExpr : Expr
	{
		[Required]
		public object Value;
	}


}
