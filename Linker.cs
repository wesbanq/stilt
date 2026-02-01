using stilt.AST;
using stilt.Errors;
using System;

namespace stilt
{
	public class Linker
	{
		public List<Scope> Modules = [];
        public List<LinkedList<Stmt>> Trees = [];

        public void Link()
        {
            
        }

        public Linker(List<Scope> modules, List<LinkedList<Stmt>> trees)
        {
            Modules = modules;
            Trees = trees;
        }
	}
}