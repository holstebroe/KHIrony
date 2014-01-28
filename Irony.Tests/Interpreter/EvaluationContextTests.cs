using Irony.Interpreter;
using Irony.Parsing;
using NUnit.Framework;

namespace Irony.Tests.Interpreter
{
  [TestFixture]
  public class EvaluationContextTests
  {
    [Test]
    public void SetValueCreatesGlobalValueIfFrameStackEmpty() {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetValue(symbol, 42);
      var actual = sut.Globals[symbol];
      Assert.That(actual, Is.EqualTo(42));
    }

    [Test]
    public void SetValueOverwritesGlobalValueIfFrameStackEmpty() {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetValue(symbol, 42);
      sut.SetValue(symbol, 24);
      var actual = sut.Globals[symbol];
      Assert.That(actual, Is.EqualTo(24));
    }

    [Test]
    public void SetValueOverwritesGlobalValueIfStackNotEmpty() {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetValue(symbol, 42);
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetValue(symbol, 24);
      var actual = sut.Globals[symbol];
      Assert.That(actual, Is.EqualTo(24));
      Assert.That(sut.CurrentFrame.Values.ContainsKey(symbol), Is.False);
    }    
    
    [Test]
    public void SetValueCreatesLocalValueIfNotFound() {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetValue(symbol, 42);
      Assert.That(sut.Globals.ContainsKey(symbol), Is.False);
      Assert.That(sut.CurrentFrame.Values[symbol], Is.EqualTo(42));
    }

    [Test]
    public void SetValueOverwritesValueAtSameStackFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetValue(symbol, 24);
      sut.SetValue(symbol, 42);
      Assert.That(sut.CurrentFrame.Values[symbol], Is.EqualTo(42));
    }  
    
    [Test]
    public void SetValueOverwritesValueAtLowerStackFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetValue(symbol, 24);
      sut.PushFrame("y", null, sut.CurrentFrame);
      sut.SetValue(symbol, 42);
      Assert.That(sut.CurrentFrame.Values.ContainsKey(symbol), Is.False);
      Assert.That(sut.CurrentFrame.Parent.Values[symbol], Is.EqualTo(42));
    }

    [Test]
    public void SetLocalValueCreatesLocalValueEvenIfSymbolExistsInLowerFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetValue(symbol, 24);
      sut.PushFrame("y", null, sut.CurrentFrame);
      sut.SetLocalValue(symbol, 42);
      Assert.That(sut.CurrentFrame.Values[symbol], Is.EqualTo(42));
      Assert.That(sut.CurrentFrame.Parent.Values[symbol], Is.EqualTo(24));
    }

    [Test]
    public void SetLocalValueOverwritesValueAtSameStackFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetLocalValue(symbol, 24);
      sut.SetLocalValue(symbol, 42);
      Assert.That(sut.CurrentFrame.Values[symbol], Is.EqualTo(42));
    }

    [Test]
    public void TryGetValueReturnsTrueIfValueExistsInCurrentFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetLocalValue(symbol, 12);
      object value;
      var actual = sut.TryGetValue(symbol, out value);
      Assert.That(actual, Is.True);
    }

    [Test]
    public void TryGetValueReturnsTrueIfValueExistsInLowerFrame()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetLocalValue(symbol, 12);
      sut.PushFrame("x", null, sut.CurrentFrame);
      object value;
      var actual = sut.TryGetValue(symbol, out value);
      Assert.That(actual, Is.True);
    }

    [Test]
    public void TryGetValueReturnsFalseIfValueDoesNotExists()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      object value;
      var actual = sut.TryGetValue(symbol, out value);
      Assert.That(actual, Is.False);
    }

    [Test]
    public void TryGetValueReadsValueFromTopFrameWhereFound()
    {
      var sut = CreateContext();
      var symbol = SymbolTable.Symbols.TextToSymbol("test");
      sut.SetLocalValue(symbol, 12);
      sut.PushFrame("x", null, sut.CurrentFrame);
      sut.SetLocalValue(symbol, 24);
      sut.PushFrame("y", null, sut.CurrentFrame);
      sut.SetLocalValue(symbol, 42);
      sut.PushFrame("z", null, sut.CurrentFrame);
      object actual;
      sut.TryGetValue(symbol, out actual);
      Assert.That(actual, Is.EqualTo(42));
    }

    private static EvaluationContext CreateContext()
    {
      return new EvaluationContext(new LanguageRuntime(new LanguageData(new Grammar())));
    }
  }
}
