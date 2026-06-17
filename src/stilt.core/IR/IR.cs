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
        ASSIGN,
        BINARY_OP,
        UNARY_OP,
        CALL_FUNC_IF_FALSE,
        INIT_VAR,
        EXECUTE_RAW,
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
        [JsonIgnore]
        public Type Type => typeof(T);
        [JsonIgnore]
        public object Value => _value!;
        [JsonProperty]
        private T _value;

        public bool Is<O>() => Type == typeof(O);
        public bool TrySetValue(object value)
        {
            if (value is not T t)
                return false;
            _value = t;
            return true;
        }

        // [JsonConstructor]
        // protected Operand() { }
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
            return CreateOperation(instruction, [.. operands.Cast<IOperand>()]);
        }
    }

    // Legacy Operation class - kept for compatibility
    public class Operation
    {
        public Instruction Instruction;
        public int OperandAmount => Operands.Count;
        public List<IOperand> Operands = [];
    }

    // New IRInstruction with target variable support
    public class IRInstruction
    {
        public VarSymbol? Target;  // Variable receiving the result (nullable for statements)
        public Instruction Opcode;
        public List<IOperand> Operands = [];
    }

    // Operand types for IR
    public class VarRefOperand : Operand<VarSymbol>
    {
        public VarRefOperand(VarSymbol var) : base(var) { }
    }

    public class BlockRefOperand : Operand<Block>
    {
        public BlockRefOperand(Block block) : base(block) { }
    }

    public class LiteralOperand : IOperand
    {
        public Type Type { get; }
        public object Value { get; }
        
        public bool Is<O>() => Type == typeof(O);
        public bool TrySetValue(object value) => false;  // Literals are immutable

        public LiteralOperand(object value, Type type)
        {
            Value = value;
            Type = type;
        }
    }

    public class Block
    {
        public string? Name;  // null = inline, non-null = emitted as function
        public List<IRInstruction> Instructions = [];
        public List<Block> ChildBlocks = [];  // Blocks that become separate functions
    }

    public class CompiledFile
    {
        public ObjectFile SourceFile;
        public Block MainBlock;
    }

    public class IRGeneratorResult
    {
        public Block MainBlock;
        public List<Block> Blocks = [];
    }

    // INCOMPLETE: identity access ported to the SymbolReference API so it compiles; several node kinds are still
    // unimplemented (see the throws below) and the generator is untested end-to-end.
    public class IRGenerator
    {
        public ProgramArgs Args;
        public ObjectFile SourceFile;
        public IRGeneratorResult Result;

        private static NamespaceMapper NamespaceMapper = new();
        private int _tempCounter = 0;
        private string _currentBlockPath = "main";

        public IRGeneratorResult Generate()
        {
            var result = new IRGeneratorResult();
            
            // Register module namespace
            if (!string.IsNullOrEmpty(SourceFile.Filepath))
            {
                var moduleName = Path.GetFileNameWithoutExtension(SourceFile.Filepath);
                NamespaceMapper.RegisterModuleNamespace(SourceFile.Filepath, NamespaceMapper.SanitizeIdentifier(moduleName));
            }

            result.MainBlock = new Block { Name = "main" };
            GenerateStatements(SourceFile.ParserResult!.Statements, result.MainBlock);
            result.Blocks = result.MainBlock.ChildBlocks;
            return result;
        }

        public void GenerateIR()
        {
            Result = Generate();
        }

        private void GenerateStatements(List<Stmt> stmts, Block block)
        {
            foreach (var stmt in stmts)
            {
                GenerateStatement(stmt, block);
            }
        }

        private void GenerateStatement(Stmt stmt, Block block)
        {
            switch (stmt)
            {
                case CompoundStmt compound:
                    GenerateStatements(compound.Statements, block);
                    break;

                case ExpressionStmt exprStmt:
                    // Lower expression and discard result
                    GenerateExpr(exprStmt.Expression, block);
                    break;

                case VarDeclStmt varDecl:
                    GenerateVarDecl(varDecl, block);
                    break;

                case ReturnStmt ret:
                    GenerateReturn(ret, block);
                    break;

                case IfStmt ifStmt:
                    GenerateIf(ifStmt, block);
                    break;

                case PreconditionLoopStmt preLoop:
                    GeneratePreconditionLoop(preLoop, block);
                    break;

                case PostconditionLoopStmt postLoop:
                    GeneratePostconditionLoop(postLoop, block);
                    break;

                case ForLoopStmt forLoop:
                    GenerateForLoop(forLoop, block);
                    break;

                case ForeachLoopStmt foreachLoop:
                    GenerateForeachLoop(foreachLoop, block);
                    break;

                case LoopStmt loop:
                    GenerateInfiniteLoop(loop, block);
                    break;

                case FuncDeclStmt funcDecl:
                    GenerateFuncDecl(funcDecl, block);
                    break;

                case ExecuteStmt exec:
                    GenerateExecute(exec, block);
                    break;

                case BreakStmt:
                case ContinueStmt:
                    // TODO: Implement break/continue with label support
                    throw new IRGenerationError("Break/Continue not yet implemented in IR generator");

                default:
                    throw new IRGenerationError($"Unhandled statement type: {stmt.GetType().Name}");
            }
        }

        private void GenerateVarDecl(VarDeclStmt varDecl, Block block)
        {
            VarSymbol? resultVar = null;
            if (varDecl.Name.Count > 0)
            {
                resultVar = varDecl.Name[0] as VarSymbol;
            }

            if (resultVar is null)
                throw new IRGenerationError("VarDeclStmt must have at least one variable name");

            if (varDecl.Value is not null)
            {
                var valueVar = GenerateExpr(varDecl.Value, block);
                var assign = new IRInstruction
                {
                    Target = resultVar,
                    Opcode = Instruction.ASSIGN,
                    Operands = [new VarRefOperand(valueVar)]
                };
                block.Instructions.Add(assign);
            }
            else
            {
                var init = new IRInstruction
                {
                    Target = resultVar,
                    Opcode = Instruction.INIT_VAR,
                    Operands = []
                };
                block.Instructions.Add(init);
            }
        }

        private void GenerateReturn(ReturnStmt ret, Block block)
        {
            var valueVar = GenerateExpr(ret.Value, block);
            // Return value is stored in a special variable or passed via convention
            // For now, we'll use ASSIGN to a return variable
            var returnVar = new VarSymbol("_return", Symbol.TempSource, SymbolReference.AlreadyResolved(ret.Value.Type));
            var assign = new IRInstruction
            {
                Target = returnVar,
                Opcode = Instruction.ASSIGN,
                Operands = [new VarRefOperand(valueVar)]
            };
            block.Instructions.Add(assign);
        }

        private void GenerateIf(IfStmt ifStmt, Block block)
        {
            var condVar = GenerateExpr(ifStmt.Condition, block);
            
            // Create blocks for then and else
            var thenBlock = new Block { Name = $"{_currentBlockPath}/if_{block.ChildBlocks.Count}_then" };
            var elseBlock = ifStmt.NextElse is not null 
                ? new Block { Name = $"{_currentBlockPath}/if_{block.ChildBlocks.Count}_else" }
                : null;

            block.ChildBlocks.Add(thenBlock);
            if (elseBlock is not null)
                block.ChildBlocks.Add(elseBlock);

            // Generate then body
            var oldPath = _currentBlockPath;
            _currentBlockPath = thenBlock.Name!;
            GenerateStatement(ifStmt.NextIf, thenBlock);
            _currentBlockPath = oldPath;

            // Generate else body if present
            if (elseBlock is not null && ifStmt.NextElse is not null)
            {
                _currentBlockPath = elseBlock.Name!;
                GenerateStatement(ifStmt.NextElse, elseBlock);
                _currentBlockPath = oldPath;
            }

            // Emit conditional call
            var thenCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC_IF_TRUE,
                Operands = [
                    new VarRefOperand(condVar),
                    new BlockRefOperand(thenBlock)
                ]
            };
            block.Instructions.Add(thenCall);

            if (elseBlock is not null)
            {
                var elseCall = new IRInstruction
                {
                    Target = null,
                    Opcode = Instruction.CALL_FUNC_IF_FALSE,
                    Operands = [
                        new VarRefOperand(condVar),
                        new BlockRefOperand(elseBlock)
                    ]
                };
                block.Instructions.Add(elseCall);
            }
        }

        private void GeneratePreconditionLoop(PreconditionLoopStmt preLoop, Block block)
        {
            var loopBlock = new Block { Name = $"{_currentBlockPath}/loop_{block.ChildBlocks.Count}_body" };
            block.ChildBlocks.Add(loopBlock);

            var oldPath = _currentBlockPath;
            _currentBlockPath = loopBlock.Name!;
            GenerateStatement(preLoop.Body, loopBlock);
            _currentBlockPath = oldPath;

            // Loop: check condition, call body if true, then tail-call self
            var condVar = GenerateExpr(preLoop.Condition, block);
            
            var loopCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC_IF_TRUE,
                Operands = [
                    new VarRefOperand(condVar),
                    new BlockRefOperand(loopBlock)
                ]
            };
            block.Instructions.Add(loopCall);

            // Add tail recursion: loop body calls itself
            var selfCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.RUN_SELF,
                Operands = []
            };
            loopBlock.Instructions.Add(selfCall);
        }

        private void GeneratePostconditionLoop(PostconditionLoopStmt postLoop, Block block)
        {
            var loopBlock = new Block { Name = $"{_currentBlockPath}/loop_{block.ChildBlocks.Count}_body" };
            block.ChildBlocks.Add(loopBlock);

            var oldPath = _currentBlockPath;
            _currentBlockPath = loopBlock.Name!;
            GenerateStatement(postLoop.Body, loopBlock);
            
            // Check condition after body
            var condVar = GenerateExpr(postLoop.Condition, loopBlock);
            _currentBlockPath = oldPath;

            // If condition true, loop again
            var loopCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC_IF_TRUE,
                Operands = [
                    new VarRefOperand(condVar),
                    new BlockRefOperand(loopBlock)
                ]
            };
            loopBlock.Instructions.Add(loopCall);

            // Initial call to loop body
            var initialCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC,
                Operands = [new BlockRefOperand(loopBlock)]
            };
            block.Instructions.Add(initialCall);
        }

        private void GenerateForLoop(ForLoopStmt forLoop, Block block)
        {
            // Initialize loop variable
            if (forLoop.LoopVariable is not null)
            {
                GenerateVarDecl(forLoop.LoopVariable, block);
            }

            var loopBlock = new Block { Name = $"{_currentBlockPath}/loop_{block.ChildBlocks.Count}_body" };
            block.ChildBlocks.Add(loopBlock);

            var oldPath = _currentBlockPath;
            _currentBlockPath = loopBlock.Name!;
            GenerateStatement(forLoop.Body, loopBlock);
            
            // Iterator expression
            if (forLoop.Iterator is not null)
            {
                GenerateExpr(forLoop.Iterator, loopBlock);
            }
            _currentBlockPath = oldPath;

            // Check condition before each iteration
            if (forLoop.Condition is not null)
            {
                var condVar = GenerateExpr(forLoop.Condition, block);
                var loopCall = new IRInstruction
                {
                    Target = null,
                    Opcode = Instruction.CALL_FUNC_IF_TRUE,
                    Operands = [
                        new VarRefOperand(condVar),
                        new BlockRefOperand(loopBlock)
                    ]
                };
                block.Instructions.Add(loopCall);
            }
            else
            {
                // Infinite loop
                var loopCall = new IRInstruction
                {
                    Target = null,
                    Opcode = Instruction.CALL_FUNC,
                    Operands = [new BlockRefOperand(loopBlock)]
                };
                block.Instructions.Add(loopCall);
            }

            // Tail recursion
            var selfCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.RUN_SELF,
                Operands = []
            };
            loopBlock.Instructions.Add(selfCall);
        }

        private void GenerateForeachLoop(ForeachLoopStmt foreachLoop, Block block)
        {
            // TODO: Implement foreach loop (requires iterator support)
            throw new IRGenerationError("ForeachLoop not yet fully implemented in IR generator");
        }

        private void GenerateInfiniteLoop(LoopStmt loop, Block block)
        {
            var loopBlock = new Block { Name = $"{_currentBlockPath}/loop_{block.ChildBlocks.Count}_body" };
            block.ChildBlocks.Add(loopBlock);

            var oldPath = _currentBlockPath;
            _currentBlockPath = loopBlock.Name!;
            GenerateStatement(loop.Body, loopBlock);
            _currentBlockPath = oldPath;

            // Initial call
            var initialCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC,
                Operands = [new BlockRefOperand(loopBlock)]
            };
            block.Instructions.Add(initialCall);

            // Tail recursion
            var selfCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.RUN_SELF,
                Operands = []
            };
            loopBlock.Instructions.Add(selfCall);
        }

        private void GenerateFuncDecl(FuncDeclStmt funcDecl, Block parentBlock)
        {
            var funcBlock = new Block { Name = funcDecl.Name.Name };
            parentBlock.ChildBlocks.Add(funcBlock);

            var oldPath = _currentBlockPath;
            _currentBlockPath = funcBlock.Name!;
            GenerateStatement(funcDecl.Value, funcBlock);
            _currentBlockPath = oldPath;

            // Function declaration doesn't emit code in parent block
            // The function block is registered and can be called via CALL_FUNC
        }

        private void GenerateExecute(ExecuteStmt exec, Block block)
        {
            var execInstr = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.EXECUTE_RAW,
                Operands = exec.Commands.Select(cmd => new LiteralOperand(cmd, typeof(string))).Cast<IOperand>().ToList()
            };
            block.Instructions.Add(execInstr);
        }

        private VarSymbol GenerateExpr(Expr expr, Block block)
        {
            switch (expr)
            {
                // Handle specific literal types before base LiteralExpr
                case ArrayLiteralExpr arr:
                case TableLiteralExpr tbl:
                    // TODO: Implement array/table literals
                    throw new IRGenerationError("Array/Table literals not yet implemented in IR generator");

                case LiteralExpr lit:
                    return GenerateLiteral(lit, block);

                case IdentityExpr id:
                    return id.Identity.Resolved as VarSymbol ?? throw new IRGenerationError("IdentityExpr must reference a VarSymbol");

                // Handle derived types before base types
                case AssignExpr assign:
                    return GenerateAssignExpr(assign, block);

                case CallExpr call:
                    return GenerateCallExpr(call, block);

                case AccessExpr:
                case NullAccessExpr:
                    // TODO: Implement member access
                    // Note: AccessExpr extends CommaExpr, so this must come before CommaExpr case
                    throw new IRGenerationError("Access expressions not yet implemented in IR generator");

                case BinaryExpr binary:
                    return GenerateBinaryExpr(binary, block);

                case UnaryExpr unary:
                    return GenerateUnaryExpr(unary, block);

                case TernaryExpr ternary:
                    return GenerateTernaryExpr(ternary, block);

                case CommaExpr comma:
                    // Evaluate all expressions, return last result
                    VarSymbol? lastResult = null;
                    foreach (var e in comma.Exprs)
                    {
                        if (e is not null)
                            lastResult = GenerateExpr(e, block);
                    }
                    return lastResult ?? throw new IRGenerationError("CommaExpr must have at least one expression");

                case FuncLiteralExpr lambda:
                    // TODO: Implement lambda functions
                    throw new IRGenerationError("Lambda functions not yet implemented in IR generator");

                default:
                    throw new IRGenerationError($"Unhandled expression type: {expr.GetType().Name}");
            }
        }

        private VarSymbol GenerateLiteral(LiteralExpr lit, Block block)
        {
            var temp = AllocateTemp(lit.Type);
            var valueType = lit.Value?.GetType() ?? typeof(object);
            var literalOp = new LiteralOperand(lit.Value ?? throw new IRGenerationError("Literal value is null"), valueType);
            var assign = new IRInstruction
            {
                Target = temp,
                Opcode = Instruction.ASSIGN,
                Operands = [literalOp]
            };
            block.Instructions.Add(assign);
            return temp;
        }

        private VarSymbol GenerateBinaryExpr(BinaryExpr binary, Block block)
        {
            var leftVar = GenerateExpr(binary.Left!, block);
            var rightVar = GenerateExpr(binary.Right!, block);
            var resultVar = AllocateTemp(binary.Type);

            var opInstr = new IRInstruction
            {
                Target = resultVar,
                Opcode = Instruction.BINARY_OP,
                Operands = [
                    new VarRefOperand(leftVar),
                    new LiteralOperand(binary.Operator?.Which ?? throw new IRGenerationError("BinaryExpr missing operator"), typeof(TokenType)),
                    new VarRefOperand(rightVar)
                ]
            };
            block.Instructions.Add(opInstr);
            return resultVar;
        }

        private VarSymbol GenerateUnaryExpr(UnaryExpr unary, Block block)
        {
            var srcVar = GenerateExpr(unary.Leaf!, block);
            var resultVar = AllocateTemp(unary.Type);

            var opInstr = new IRInstruction
            {
                Target = resultVar,
                Opcode = Instruction.UNARY_OP,
                Operands = [
                    new LiteralOperand(unary.Operator?.Which ?? throw new IRGenerationError("UnaryExpr missing operator"), typeof(TokenType)),
                    new VarRefOperand(srcVar)
                ]
            };
            block.Instructions.Add(opInstr);
            return resultVar;
        }

        private VarSymbol GenerateTernaryExpr(TernaryExpr ternary, Block block)
        {
            var condVar = GenerateExpr(ternary.Left!, block);
            var thenVar = GenerateExpr(ternary.Middle!, block);
            var elseVar = GenerateExpr(ternary.Right!, block);
            var resultVar = AllocateTemp(ternary.Type);

            // Create blocks for then/else
            var thenBlock = new Block { Name = $"{_currentBlockPath}/ternary_{block.ChildBlocks.Count}_then" };
            var elseBlock = new Block { Name = $"{_currentBlockPath}/ternary_{block.ChildBlocks.Count}_else" };
            block.ChildBlocks.Add(thenBlock);
            block.ChildBlocks.Add(elseBlock);

            // Then: assign thenVar to result
            var thenAssign = new IRInstruction
            {
                Target = resultVar,
                Opcode = Instruction.ASSIGN,
                Operands = [new VarRefOperand(thenVar)]
            };
            thenBlock.Instructions.Add(thenAssign);

            // Else: assign elseVar to result
            var elseAssign = new IRInstruction
            {
                Target = resultVar,
                Opcode = Instruction.ASSIGN,
                Operands = [new VarRefOperand(elseVar)]
            };
            elseBlock.Instructions.Add(elseAssign);

            // Conditional call
            var thenCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC_IF_TRUE,
                Operands = [
                    new VarRefOperand(condVar),
                    new BlockRefOperand(thenBlock)
                ]
            };
            block.Instructions.Add(thenCall);

            var elseCall = new IRInstruction
            {
                Target = null,
                Opcode = Instruction.CALL_FUNC_IF_FALSE,
                Operands = [
                    new VarRefOperand(condVar),
                    new BlockRefOperand(elseBlock)
                ]
            };
            block.Instructions.Add(elseCall);

            return resultVar;
        }

        private VarSymbol GenerateAssignExpr(AssignExpr assign, Block block)
        {
            var rhsVar = GenerateExpr(assign.Right!, block);
            
            // LHS must be a variable reference (IdentityExpr)
            if (assign.Left is not IdentityExpr lhsId)
                throw new IRGenerationError("Assignment LHS must be a variable reference");

            if (lhsId.Identity.Resolved is not VarSymbol lhsVar)
                throw new IRGenerationError("Assignment LHS must reference a VarSymbol");

            // Handle compound assignment operators
            if (assign.Operation.HasValue && assign.Operation.Value != TokenType.Assign)
            {
                // Lower compound assignment: x += y -> x = x + y
                var tempOp = AllocateTemp(assign.Type);
                var binaryOp = new IRInstruction
                {
                    Target = tempOp,
                    Opcode = Instruction.BINARY_OP,
                    Operands = [
                        new VarRefOperand(lhsVar),
                        new LiteralOperand(assign.Operation.Value, typeof(TokenType)),
                        new VarRefOperand(rhsVar)
                    ]
                };
                block.Instructions.Add(binaryOp);
                rhsVar = tempOp;
            }

            var assignInstr = new IRInstruction
            {
                Target = lhsVar,
                Opcode = Instruction.ASSIGN,
                Operands = [new VarRefOperand(rhsVar)]
            };
            block.Instructions.Add(assignInstr);

            return lhsVar;
        }

        private VarSymbol GenerateCallExpr(CallExpr call, Block block)
        {
            // Left is function reference, Right is arguments (CommaExpr)
            if (call.Left is not IdentityExpr funcId)
                throw new IRGenerationError("Function call must reference a function");

            var funcSymbol = funcId.Identity.Resolved as VarSymbol;
            if (funcSymbol is null)
                throw new IRGenerationError("Function call must reference a VarSymbol");

            var resultVar = AllocateTemp(call.Type);

            // Collect arguments
            var args = new List<IOperand>();
            if (call.Right is CommaExpr commaArgs)
            {
                foreach (var arg in commaArgs.Exprs)
                {
                    if (arg is not null)
                    {
                        var argVar = GenerateExpr(arg, block);
                        args.Add(new VarRefOperand(argVar));
                    }
                }
            }

            var callInstr = new IRInstruction
            {
                Target = resultVar,
                Opcode = Instruction.CALL_FUNC,
                Operands = [new VarRefOperand(funcSymbol), .. args]
            };
            block.Instructions.Add(callInstr);

            return resultVar;
        }

        private VarSymbol AllocateTemp(TypeSymbol type)
        {
            var tempName = $"_t{_tempCounter++}";
            return new VarSymbol(tempName, Symbol.TempSource, SymbolReference.AlreadyResolved(type));
        }

        public IRGenerator(ProgramArgs args, ObjectFile file)
        {
            Args = args;
            SourceFile = file;
        }
 
        public IRGenerator(IRGeneratorResult result)
        {
            Result = result;
        }
    }
}