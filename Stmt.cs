using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace stilt.AST
{
	public abstract class Stmt
	{
		public required Scope Scope;
	}

	public class CompoundStmt : Stmt 
	{
		public LinkedList<Stmt> Statements = new();
	}

	public class IfStmt : Stmt
	{
		public required Expr Condition;
		public required Stmt NextIf;
		public Stmt? NextElse;
	}

	public class ExpressionStmt : Stmt
	{
		public required Expr Expression;
	}

	public class VarDeclStmt : Stmt
	{
		public required Symbol Name;
		public bool IsConst = false;
		public Expr? Value;
	}

	public class TypeDeclStmt : Stmt
	{
		public required TypeSymbol Name;
		public Stmt Value;
	}

	public class FuncDeclStmt : Stmt
	{
		public required Symbol Name;
		public List<VarSymbol> Arguments = new();
		public Stmt Value;
	}
}
