using stilt.Errors;
using stilt.AST;

namespace stilt.IR
{
    public enum Instruction : ushort
    {
        NOOP,
        CALL_FUNC,
        INIT_STACK_VAR,
        LOAD_SB,
        LOAD_DS,
        CALL_FUNC_IF_TRUE,
        RUN_SELF,
    }

    public interface IOperand
    {
        public Type Type { get; }
        public object Value { get; }
        public bool Is<O>();
        public bool TrySetValue(object value);
    }

    public class Operand<T> : IOperand
    {
        public Type Type => typeof(T);
        public object Value => _value!;
        private T _value;

        public bool Is<O>() => Type == typeof(O);
        public bool TrySetValue(object value)
        {
            if (value is not T t)
                return false;
            _value = t;
            return true;
        }

        public Operand(T value)
        {
            _value = value;
        }
    }

    public static class OperationFactory
    {
        private static Operation CreateOperation(Instruction instruction, params IOperand[] operands)
        {
            return new Operation
            {
                Instruction = instruction,
                Operands = [.. operands],
            };
        }

        private static bool CheckTypes(object[] operands, Type[] expectedOperandTypes)
        {
            if (expectedOperandTypes.Length == 0)
                return true;
            if (operands.Length != expectedOperandTypes.Length)
                return false;
            for (int i = 0; i < operands.Length; i++)
            {
                if (operands[i].GetType() != expectedOperandTypes[i])
                    return false;
            }
            return true;
        }

        public static Operation New(Instruction instruction, params object[] operands)
        {
            // Type[] expectedOperandTypes = instruction switch
            // {
            //     Instruction.NOOP => [],
            //     Instruction.CALL_FUNC => [typeof(VarSymbol)],
            //     Instruction.INIT_STACK_VAR => [],
            //     Instruction.LOAD_SB => [],
            //     Instruction.LOAD_DS => [],
            //     Instruction.CALL_FUNC_IF_TRUE => [],
            // };

            // if (!CheckTypes(operands, expectedOperandTypes))
            //     throw new IRGenerationError($"Invalid operands for instruction: {instruction}");

            return CreateOperation(instruction, [.. operands.Cast<IOperand>()]);
        }
    }

    public class Operation
    {
        public Instruction Instruction;
        public int OperandAmount => Operands.Count;
        public List<IOperand> Operands = [];
    }

    public class Block
    {
        public List<Operation> Operations = [];
    }

    public class CompiledFile
    {
        public ParsedFile SourceFile;
        public Block MainBlock;
    }

    public class IRGenerator
    {
        public ProgramArgs Args;
        public ParsedFile SourceFile;
        public Block MainBlock;
        public List<Block> Blocks = [];

        public void GenerateIR()
        {
            MainBlock = GenerateBlock(SourceFile.Parser.Statements);
        }

        private Block GenerateBlock(List<Stmt> stmts)
        {
            var block = new Block();

            foreach (var stmt in stmts)
            {
                switch (stmt)
                {
                    case LoopStmt loopStmt:
                    {
                        
                        
                        break;
                    }
                }
            }

            return block;
        }

        private Operation GenerateOperation(Stmt stmt)
        {
            
        }

        public IRGenerator(ProgramArgs args, ParsedFile file)
        {
            Args = args;
            SourceFile = file;
        }
    }
}