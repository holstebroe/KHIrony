using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Irony.Ast;
using Irony.Interpreter;
using Irony.Parsing;
using NUnit.Framework;

namespace Irony.Tests.Ast
{
  [TestFixture]
  public class LocalIdentifierNodeTests
  {
    [Test]
    public void EvaluateNodeReadsFromTopMostSymbolInFrameStack() {
      // Create context 
      var context = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("id");
      // Insert value in lower frame
      context.SetLocalValue(symbol, 24);
      context.PushFrame("x", null, context.CurrentFrame);
      // Insert value in top frame
      context.SetLocalValue(symbol, 42);
      context.PushFrame("y", null, context.CurrentFrame);

      // Try evaluating
      var sut = new LocalIdentifierNode();
      var token = new TestToken(symbol);
      sut.Init(null, new ParseTreeNode(token));
      sut.EvaluateNode(context, AstMode.Read);

      var actual = context.LastResult;
      Assert.That(actual, Is.EqualTo(42));
    }

    [Test]
    public void EvaluateNodeWritesToTopFrameStack() {
      // Create context 
      var context = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("id");
      // Insert value in lower frame
      context.SetLocalValue(symbol, 24);
      context.PushFrame("x", null, context.CurrentFrame);
      // Insert value in top frame
      context.SetLocalValue(symbol, 42);
      context.PushFrame("y", null, context.CurrentFrame);

      context.Data.Push(100);

      // Try evaluating
      var sut = new LocalIdentifierNode();
      var token = new TestToken(symbol);
      sut.Init(null, new ParseTreeNode(token));
      sut.EvaluateNode(context, AstMode.Write);

      var actual = context.CurrentFrame.Values[symbol];
      Assert.That(actual, Is.EqualTo(100));
      // Check that parent value is untouched. This is what differentiate LocalIdentifierNode from IdentifierNode
      Assert.That(context.CurrentFrame.Parent.Values[symbol], Is.EqualTo(42));
    }

    private static EvaluationContext CreateContext() {
      return new EvaluationContext(new LanguageRuntime(new LanguageData(new Grammar())));
    }

    private class TestToken : Token {
      public TestToken(Symbol symbol) : base(new Terminal("testTerminal"), new SourceLocation(), "token", "value") {
        Symbol = symbol;
      }
    }
  }
}
