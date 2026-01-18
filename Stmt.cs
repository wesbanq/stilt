using System;
using System.Collections.Generic;
using System.Text;

namespace stilt.AST
{
	public abstract class Stmt
	{
		public Stmt? Next;
	}
	
	public class CompoundStmt : Stmt { }

	public class IfStmt : Stmt
	{
		public Expr Condition;
		public Stmt NextIfTrue;
		public Stmt? NextElse;
	}

	public class ExpressionStmt : Stmt
	{
		public Expr Expression;
	}
}
