#region License
/* **********************************************************************************
 * Copyright (c) Roman Ivantsov
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution. 
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the 
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Irony.Ast; 
using Irony.Parsing;

namespace Irony.Interpreter {

  public enum InterpreterStatus {
    Ready,
    Evaluating,
    WaitingMoreInput, //command line only
    SyntaxError,
    RuntimeError,
    Canceled,
    Aborted
  }

  public class ScriptInterpreter {
    #region Fields and properties
    public readonly LanguageData Language;
    public readonly LanguageRuntime Runtime; 
    public readonly EvaluationContext EvaluationContext;
    public readonly Parser Parser;

    public Thread WorkerThread { get; private set; }
    public Exception LastException {get; private set;}

    public InterpreterStatus Status { get; private set; }
    
    public bool RethrowExceptions = true;
    public bool PrintParseErrors = true;

    private CancellationTokenSource _cancellationTokenSource;

    private readonly ManualResetEvent _waitHandle = new ManualResetEvent(true);
    public WaitHandle WaitHandle { get { return _waitHandle; } }

    public ParseMode ParseMode {
      get { return Parser.Context.Mode; }
      set { Parser.Context.Mode = value; }
    }
    public ValuesTable Globals { 
      get { return EvaluationContext.TopFrame.Values; } 
    }
    //internal, real status of interpreter. The public Status field gets updated only on exit from public methods
    // We want to make sure external code sees interpreter as BUSY until we actually completed operation internally 
    private InterpreterStatus _internalStatus;     
    #endregion 

    #region constructors
    public ScriptInterpreter(Grammar grammar) : this(new LanguageData(grammar)) { }

    public ScriptInterpreter(LanguageData language) {
      Language = language;
      Runtime = Language.Grammar.CreateRuntime(Language);
      Parser = new Parser(Language);
      EvaluationContext = new EvaluationContext(Runtime);
      Status = _internalStatus = InterpreterStatus.Ready;
    }
    #endregion 

    #region Evaluate overloads
    public void Evaluate(string script) {
      Script = script;
      Evaluate(); 
    }
    public void Evaluate(ParseTree parsedScript) {
      ParsedScript = parsedScript;
      Evaluate(); 
    }
    public void Evaluate() {
      try {
        _waitHandle.Reset();
        _internalStatus = Status = InterpreterStatus.Evaluating;
        ParseAndEvaluate(); 
      } finally {
        Status = _internalStatus;
        _waitHandle.Set();
      }
    }

    public void EvaluateAsync(string script) {
      Script = script;
      EvaluateAsync(); 
    }
    public void EvaluateAsync(ParseTree parsedScript) {
      ParsedScript = parsedScript;
      EvaluateAsync(); 
    }
    public void EvaluateAsync() {
      CheckNotBusy();
      _waitHandle.Reset();
      Status = _internalStatus = InterpreterStatus.Evaluating;
      WorkerThread = new Thread(AsyncThreadStart);
      WorkerThread.Start(null);
    }
    #endregion 

    #region Other public members: Script, ParsedScript, IsBusy(), GetOutput()
    public string Script {
      get { return _script; }
      set {
        CheckNotBusy(); 
        _script = value;
        _parsedScript = null; 
      }
    } string _script;
 
    public ParseTree ParsedScript {
      get { return _parsedScript; }
      set { 
        _parsedScript = value;
        _script = (_parsedScript == null ? null : _parsedScript.SourceText); 
      }
    }

    ParseTree _parsedScript;

    public bool IsBusy() {
      return Status == InterpreterStatus.Evaluating;
    }

    public string GetOutput() {
      return EvaluationContext.OutputBuffer.ToString(); 
    }
    public void ClearOutputBuffer() {
      EvaluationContext.OutputBuffer.Length = 0;
    }

    public ParserMessageList GetParserMessages() {
      if (ParsedScript == null)
        return new ParserMessageList();
      else
        return ParsedScript.ParserMessages;
    }

    /// <summary>
    /// Signals the interpreter to cancel at the next AstNode evaluation.
    /// The script will be aborted if the <paramref name="cancelTimeout"/> is exceeded.
    /// Use <see cref="Status"/> to determine if the script was canceled, aborted or terminated before abortion.
    /// </summary>
    public void Abort(TimeSpan cancelTimeout) {
      try {
        if (_cancellationTokenSource != null)
          _cancellationTokenSource.Cancel();
        _cancellationTokenSource = null;
        if (WorkerThread == null) return;
        WorkerThread.Join((int) cancelTimeout.TotalMilliseconds);
        if (WorkerThread.IsAlive) WorkerThread.Abort();
      } catch { }
      WorkerThread = null;
      WaitHandle.WaitOne();
    }

    public void Abort()
    {
      Abort(TimeSpan.FromSeconds(5));
    }
    #endregion 

    #region private implementations -------------------------------------------------------------------------------
    private void AsyncThreadStart(object data) {
      try {
        ParseAndEvaluate();
      }
      catch {
        // Eat rethrown runtime exceptions. Uncaught thread exceptions may bring the whole application down.
        // The user should check Status and LastException instead. 
      }
      finally {
        Status = _internalStatus;
        _waitHandle.Set();
      }
    }
    private void CheckNotBusy() {
      if (IsBusy())
        throw new Exception(Resources.ErrInterpreterIsBusy);
    }

    private void ParseAndEvaluate() {
      EvaluationContext.EvaluationTime = 0;
      try
      {
        LastException = null;
        if (ParsedScript == null)
        {
          //don't evaluate empty strings, just return
          if (Script == null || Script.Trim() == string.Empty && Status == InterpreterStatus.Ready) return;
          ParsedScript = this.Parser.Parse(Script, "source");
          CheckParseStatus();
          if (_internalStatus != InterpreterStatus.Evaluating) return;
        }
        if (ParsedScript == null)
          return;
        EvaluateParsedScript();
        _internalStatus = InterpreterStatus.Ready;
        _cancellationTokenSource = null;
      }
      catch (OperationCanceledException)
      {
        _internalStatus = InterpreterStatus.Canceled;
      }
      catch (ThreadAbortException)
      {
        _internalStatus = InterpreterStatus.Aborted;
      }
      catch (Exception ex) {
        LastException = ex;
        _internalStatus = InterpreterStatus.RuntimeError;
        if (LastException != null && RethrowExceptions)
          throw;
      }
    }

    private void EvaluateParsedScript() {
        var iRoot = GetAstInterface();
        if (iRoot == null) return; 
        EvaluationContext.ClearLastResult();
        _cancellationTokenSource = new CancellationTokenSource();
        EvaluationContext.CancellationToken = _cancellationTokenSource.Token;
        var start = Environment.TickCount;
        iRoot.Evaluate(EvaluationContext, AstMode.Read);
        EvaluationContext.EvaluationTime = Environment.TickCount - start;
        if (EvaluationContext.HasLastResult)
          EvaluationContext.Write(EvaluationContext.LastResult + Environment.NewLine);
    }

    private IInterpretedAstNode GetAstInterface()  {
        Check(ParsedScript != null, Resources.ErrParseTreeNull);
        Check(ParsedScript.Root != null, Resources.ErrParseTreeRootNull);
        var astNode = ParsedScript.Root.AstNode;
        Check(astNode != null, Resources.ErrRootAstNodeNull);
        var iInterpNode = astNode as IInterpretedAstNode;
        Check(iInterpNode != null, Resources.ErrRootAstNoInterface);
        return iInterpNode;
    }

    private bool CheckParseStatus() {
      if (ParsedScript == null) return false;
      if (ParsedScript.HasErrors()) {
        _internalStatus = InterpreterStatus.SyntaxError;
        if (PrintParseErrors) {
          foreach(var err in ParsedScript.ParserMessages) {
            var msg = string.Format(Resources.ErrOutErrorPrintFormat, err.Location.ToUiString(),  err.Message);
            this.EvaluationContext.OutputBuffer.AppendLine(msg);
          }//foreach
        }//if
        return false;
      }
      switch (ParsedScript.Status) {
        case ParseTreeStatus.Error:
          _internalStatus = InterpreterStatus.SyntaxError;
          return false;
        case ParseTreeStatus.Partial:
          _internalStatus = InterpreterStatus.WaitingMoreInput;
          return false;
        default:
          _internalStatus = InterpreterStatus.Evaluating;
          return true;
      }
    }

    private static void Check(bool condition, string message) {
      if (!condition)
        throw new Exception(message);
    }

    #endregion 


  }//class

}//namespace
