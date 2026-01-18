using System;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public abstract class Expr { }
	
	public class IdentitiyExpr : Expr 
	{
		public Symbol Identity;
	}

	public class AccessExpr : IdentitiyExpr
	{
		public AccessExpr? From;
	}

	public class Symbol 
	{
		public string Name;
		public string Source;
	}

	public abstract class BinaryExpr : Expr
	{
		public Expr Left;
		public Expr Right;
		//public Token Operation;
	}

	public abstract class UnaryExpr : Expr
	{
		public Expr Leaf;
		//public Token Operation;
	}

	public class IncrementExpr : UnaryExpr { }
	public class DecrementExpr : UnaryExpr { }
	public class NegationExpr : UnaryExpr { }

	public class AdditionExpr : BinaryExpr { }
	public class SubtractionExpr : BinaryExpr { }
	public class DivisionExpr : BinaryExpr { }
	public class MultiplicationExpr : BinaryExpr { }

	public class ArrayExpr : Expr
	{
		public List<Expr> Array = new List<Expr>();
	}

	public class CallExpr : Expr
	{
		public Symbol Function;
		public List<Expr> Arguments = new List<Expr>();
	}

	public class LiteralExpr : Expr
	{
		public object Value;
	}


}
