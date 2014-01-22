using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Irony.Ast;
using Irony.Interpreter;
using Irony.Parsing;
using NUnit.Framework;

namespace Irony.Tests.Interpreter
{
  public class MiniGrammar : Grammar
  {
    public MiniGrammar()
    {
      // 1. Terminals
      var identifier = new IdentifierTerminal("id");
      var number = new NumberLiteral("number");

      // 2. Non-terminals
      var StmtList = new NonTerminal("StmtList", typeof(StatementListNode));
      var Stmt = new NonTerminal("Stmt");
      var Expr = new NonTerminal("Expr");
      var FunctionCall = new NonTerminal("FunctionCall", typeof (FunctionCallNode));

      // 3. BNF rules
      StmtList.Rule = MakePlusRule(StmtList, Stmt);
      Stmt.Rule = Expr + ToTerm(";");
      Expr.Rule = FunctionCall | number;
      FunctionCall.Rule = identifier + "(" + ")";

      MarkPunctuation("(", ")", ";");
      RegisterBracePair("(", ")");
      MarkTransient(Stmt, Expr);

      Root = StmtList;
      LanguageFlags = LanguageFlags.CreateAst;
    }
  }

    [TestFixture]
    public class EvaluateAsyncTests
    {
        [Test]
        public void MiniGrammarFunctionCall()
        {
            AssertAstTree(new MiniGrammar(), "func1();func2();",
                NewNode<StatementListNode>(),
                NewNode<FunctionCallNode>(),
                NewNode<IdentifierNode>("func1"),
                NewNode<FunctionCallNode>(),
                NewNode<IdentifierNode>("func2"));
        }
        [Test]
        public void ScriptCanBeCanceled()
        {
            var interpreter = new ScriptInterpreter(new MiniGrammar());
            bool func2IsCalled = false;
            interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func1"), new ActionCallTarget(() => Thread.Sleep(500)));
            interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func2"), new ActionCallTarget(() => func2IsCalled = true));
            interpreter.EvaluateAsync("func1();func2();");
            Thread.Sleep(100); // Give the parser time to finish and evaluation to start.
            interpreter.Abort(TimeSpan.FromMilliseconds(1000));

            Assert.That(interpreter.Status, Is.EqualTo(InterpreterStatus.Canceled));
            Assert.That(func2IsCalled, Is.False);
        }        
      
      [Test]
      public void ScriptIsAbortedIfNotCanceledWithinTimeout()
      {
          var interpreter = new ScriptInterpreter(new MiniGrammar());
          bool func2IsCalled = false;
          interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func1"), new ActionCallTarget(() => Thread.Sleep(500)));
          interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func2"), new ActionCallTarget(() => func2IsCalled = true));
          interpreter.EvaluateAsync("func1();func2();");
          Thread.Sleep(100); // Give the parser time to finish and evaluation to start.
          interpreter.Abort(TimeSpan.FromMilliseconds(1));

          Assert.That(interpreter.Status, Is.EqualTo(InterpreterStatus.Aborted));
          Assert.That(func2IsCalled, Is.False);
      }

      [Test]
      public void WaitHandleWillBeSetWhenScriptCompletes()
      {
        var interpreter = new ScriptInterpreter(new MiniGrammar());
        bool func2IsCalled = false;
        interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func1"), new ActionCallTarget(() => Thread.Sleep(0)));
        interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func2"), new ActionCallTarget(() => func2IsCalled = true));
        interpreter.EvaluateAsync("func1();func2();");
        interpreter.WaitHandle.WaitOne();

        Assert.That(interpreter.Status, Is.EqualTo(InterpreterStatus.Ready));
        Assert.That(func2IsCalled, Is.True);        
      }

      [Test]
      public void LastResultContainsLastExpressionWhenScriptCompletes()
      {
        var interpreter = new ScriptInterpreter(new MiniGrammar());
        interpreter.EvaluateAsync("5;42;");
        interpreter.WaitHandle.WaitOne();

        Assert.That(interpreter.Status, Is.EqualTo(InterpreterStatus.Ready));
        Assert.That(interpreter.EvaluationContext.LastResult, Is.EqualTo(42));
      }

      [Test]
      public void StatusIsRunTimeErrorIfExecutionFails()
      {
        var interpreter = new ScriptInterpreter(new MiniGrammar());
        interpreter.EvaluationContext.SetValue(SymbolTable.Symbols.TextToSymbol("func1"), new ActionCallTarget(() => { throw new Exception(); }));
        interpreter.EvaluateAsync("func1();");
        interpreter.WaitHandle.WaitOne();

        Assert.That(interpreter.Status, Is.EqualTo(InterpreterStatus.RuntimeError));
      }


        public class ActionCallTarget : ICallTarget
        {
            private readonly Action _action;

            public ActionCallTarget(Action action)
            {
                _action = action;
            }

            public void Call(EvaluationContext context)
            {
                _action();
            }
        }

        protected ParseTree AssertAstTree(Grammar grammar, string script, params ComparableAstNode[] nodes)
        {
            var parser = new Parser(grammar);
            var parseTree = parser.Parse(script);

            Assert.That(parseTree.Status, Is.EqualTo(ParseTreeStatus.Parsed), FormatParseError(parseTree));
            var astNodes = UnfoldAstTree((AstNode)parseTree.Root.AstNode);
            var comparableNodes = astNodes.Select(x => new ComparableAstNode(x.GetType(), x.AsString)).ToArray();
            Assert.That(comparableNodes, Is.EqualTo(nodes));
            return parseTree;
        }

        protected IEnumerable<AstNode> UnfoldAstTree(AstNode node)
        {
            yield return node;
            foreach (var childNode in node.ChildNodes)
            {
                var subNodes = UnfoldAstTree(childNode);
                foreach (var subNode in subNodes)
                {
                    yield return subNode;
                }
            }
        }

        protected class ComparableAstNode : IEquatable<ComparableAstNode>
        {
            private readonly Type _astType;
            private readonly string _value;

            public ComparableAstNode(Type astType, string value)
            {
                this._astType = astType;
                this._value = value;
            }

            public bool Equals(ComparableAstNode other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                if (_value != null)
                    return Equals(_astType, other._astType) && string.Equals(_value, other._value);
                return Equals(_astType, other._astType);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj is ComparableAstNode)
                    return Equals((ComparableAstNode)obj);
                if (obj is AstNode)
                    return Equals(new ComparableAstNode(obj.GetType(), ((AstNode)obj).AsString));
                return false;
            }

            public override string ToString()
            {
                if (_value == null)
                    return _astType.FullName;
                return string.Format("{0}:{1}", _astType.FullName, _value);
            }
        }

        protected ComparableAstNode NewNode<T>(string value = null)
        {
            return new ComparableAstNode(typeof(T), value);
        }

        protected string FormatParseError(ParseTree result)
        {
            if (!result.HasErrors()) return string.Empty;
            return string.Join("\n", result.ParserMessages);
        }

    }
}
