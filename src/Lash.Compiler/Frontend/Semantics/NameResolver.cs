namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;

public sealed class NameResolver {
  private static readonly HashSet<char> ValidSetShortFlags = [
    'a', 'b', 'e', 'f', 'h', 'k', 'm', 'n', 'p', 't', 'u', 'v', 'x', 'B', 'C',
    'E', 'H', 'P', 'T'
  ];

  private static readonly HashSet<string> ValidSetLongOptions =
      new(StringComparer.Ordinal) { "allexport",
                                    "braceexpand",
                                    "emacs",
                                    "errexit",
                                    "errtrace",
                                    "functrace",
                                    "hashall",
                                    "histexpand",
                                    "history",
                                    "ignoreeof",
                                    "interactive-comments",
                                    "keyword",
                                    "monitor",
                                    "noclobber",
                                    "noexec",
                                    "noglob",
                                    "nolog",
                                    "notify",
                                    "nounset",
                                    "onecmd",
                                    "physical",
                                    "pipefail",
                                    "posix",
                                    "privileged",
                                    "verbose",
                                    "vi",
                                    "xtrace" };

  private static readonly HashSet<char> ValidShoptShortFlags =
      ['o', 'p', 'q', 's', 'u'];

  private static readonly HashSet<char> ValidExportShortFlags = ['f', 'n', 'p'];

  private static readonly HashSet<char> ValidAliasShortFlags = ['p'];

  private static readonly HashSet<string> ValidTrapSignals =
      new(StringComparer.Ordinal) {
        "EXIT", "ERR",    "DEBUG", "RETURN", "HUP",  "INT",    "QUIT",
        "ILL",  "TRAP",   "ABRT",  "BUS",    "FPE",  "KILL",   "USR1",
        "SEGV", "USR2",   "PIPE",  "ALRM",   "TERM", "STKFLT", "CHLD",
        "CONT", "STOP",   "TSTP",  "TTIN",   "TTOU", "URG",    "XCPU",
        "XFSZ", "VTALRM", "PROF",  "WINCH",  "IO",   "PWR",    "SYS"
      };

  private readonly DiagnosticBag diagnostics;
  private readonly Dictionary<string, SymbolInfo> globalScope;
  private readonly HashSet<string> globalDeclared = new(StringComparer.Ordinal);
  private readonly Dictionary<string, HashSet<string>> enums =
      new(StringComparer.Ordinal);
  private readonly Dictionary<string, FunctionInfo> functions =
      new(StringComparer.Ordinal);
  private readonly Stack<Dictionary<string, SymbolInfo>> scopes = new();
  private readonly Stack<HashSet<string>> declaredInScope = new();
  private int loopDepth;
  private int functionDepth;

  public NameResolver(DiagnosticBag diagnostics) {
    this.diagnostics = diagnostics;
    globalScope = new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);
    scopes.Push(globalScope);
    declaredInScope.Push(globalDeclared);
  }

  public void Analyze(ProgramNode program) {
    CollectDeclarations(program.Statements);

    foreach (var statement in program.Statements)
      CheckStatement(statement);
  }

  private void CollectDeclarations(IEnumerable<Statement> statements) {
    foreach (var statement in statements) {
      switch (statement) {
      case FunctionDeclaration function:
        if (functions.ContainsKey(function.Name)) {
          Report(function, $"Duplicate function declaration '{function.Name}'.",
                 DiagnosticCodes.DuplicateDeclaration);
        } else {
          var required = function.Parameters.Count(p => p.DefaultValue == null);
          functions[function.Name] =
              new FunctionInfo(function.Parameters.Count, required);
        }

        CollectDeclarations(function.Body);
        break;

      case EnumDeclaration enumDeclaration:
        if (enums.ContainsKey(enumDeclaration.Name)) {
          Report(enumDeclaration,
                 $"Duplicate enum declaration '{enumDeclaration.Name}'.",
                 DiagnosticCodes.DuplicateDeclaration);
        } else {
          enums[enumDeclaration.Name] = new HashSet<string>(
              enumDeclaration.Members, StringComparer.Ordinal);
        }
        break;

      case IfStatement ifStatement:
        CollectDeclarations(ifStatement.ThenBlock);
        foreach (var elifClause in ifStatement.ElifClauses)
          CollectDeclarations(elifClause.Body);
        CollectDeclarations(ifStatement.ElseBlock);
        break;

      case SwitchStatement switchStatement:
        foreach (var clause in switchStatement.Cases)
          CollectDeclarations(clause.Body);
        break;

      case ForLoop forLoop:
        CollectDeclarations(forLoop.Body);
        break;
      case SelectLoop selectLoop:
        CollectDeclarations(selectLoop.Body);
        break;

      case WhileLoop whileLoop:
        CollectDeclarations(whileLoop.Body);
        break;

      case UntilLoop untilLoop:
        CollectDeclarations(untilLoop.Body);
        break;

      case SubshellStatement subshellStatement:
        CollectDeclarations(subshellStatement.Body);
        break;
      }
    }
  }

  private void CheckStatement(Statement statement) {
    switch (statement) {
    case VariableDeclaration variable:
      CheckExpression(variable.Value);
      if (IsBuiltinIdentifier(variable.Name)) {
        Report(variable, $"Cannot declare built-in variable '{variable.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
        break;
      }

      Declare(variable.Name, variable.Kind == VariableDeclaration.VarKind.Const,
              variable, variable.IsGlobal);
      break;

    case EnumDeclaration:
      break;

    case Assignment assignment:
      CheckExpression(assignment.Value);

      if (assignment.Target is IdentifierExpression identifier)
        ValidateAssignmentTarget(identifier, assignment.IsGlobal);
      else if (assignment.Target is IndexAccessExpression indexAccess)
        ValidateIndexAssignmentTarget(indexAccess);
      break;

    case FunctionDeclaration function:
      CheckFunction(function);
      break;

    case IfStatement ifStatement:
      CheckExpression(ifStatement.Condition);
      PushScope();
      foreach (var nested in ifStatement.ThenBlock)
        CheckStatement(nested);
      PopScope();

      foreach (var elifClause in ifStatement.ElifClauses) {
        CheckExpression(elifClause.Condition);
        PushScope();
        foreach (var nested in elifClause.Body)
          CheckStatement(nested);
        PopScope();
      }

      PushScope();
      foreach (var nested in ifStatement.ElseBlock)
        CheckStatement(nested);
      PopScope();
      break;

    case SwitchStatement switchStatement:
      CheckExpression(switchStatement.Value);
      foreach (var clause in switchStatement.Cases) {
        CheckExpression(clause.Pattern);
        PushScope();
        foreach (var nested in clause.Body)
          CheckStatement(nested);
        PopScope();
      }
      break;

    case ForLoop forLoop:
      if (forLoop.Range != null)
        CheckExpression(forLoop.Range);
      if (forLoop.Step != null)
        CheckExpression(forLoop.Step);

      PushScope();
      loopDepth++;
      Declare(forLoop.Variable, isConst: false, forLoop);
      foreach (var nested in forLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case SelectLoop selectLoop:
      if (selectLoop.Options != null)
        CheckExpression(selectLoop.Options);

      PushScope();
      loopDepth++;
      Declare(selectLoop.Variable, isConst: false, selectLoop);
      foreach (var nested in selectLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case WhileLoop whileLoop:
      CheckExpression(whileLoop.Condition);
      PushScope();
      loopDepth++;
      foreach (var nested in whileLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case UntilLoop untilLoop:
      CheckExpression(untilLoop.Condition);
      PushScope();
      loopDepth++;
      foreach (var nested in untilLoop.Body)
        CheckStatement(nested);
      loopDepth--;
      PopScope();
      break;

    case BreakStatement:
      if (loopDepth == 0) {
        Report(statement, "'break' can only be used inside a loop.",
               DiagnosticCodes.InvalidControlFlowContext);
      }
      break;

    case ContinueStatement:
      if (loopDepth == 0) {
        Report(statement, "'continue' can only be used inside a loop.",
               DiagnosticCodes.InvalidControlFlowContext);
      }
      break;

    case ReturnStatement returnStatement:
      if (functionDepth == 0) {
        Report(returnStatement, "'return' can only be used inside a function.",
               DiagnosticCodes.InvalidControlFlowContext);
      }

      if (returnStatement.Value != null)
        CheckExpression(returnStatement.Value);
      break;

    case ShiftStatement shiftStatement when shiftStatement.Amount != null:
      CheckExpression(shiftStatement.Amount);
      break;

    case SubshellStatement subshellStatement:
      PushScope();
      foreach (var nested in subshellStatement.Body)
        CheckStatement(nested);
      PopScope();

      ResolveIntoBinding(subshellStatement.IntoVariable,
                         subshellStatement.IntoMode, subshellStatement,
                         (creates, createConst) => {
                           subshellStatement.IntoCreatesVariable = creates;
                           subshellStatement.IntoCreatesConst = createConst;
                         });
      break;

    case CoprocStatement coprocStatement:
      PushScope();
      foreach (var nested in coprocStatement.Body)
        CheckStatement(nested);
      PopScope();

      ResolveIntoBinding(coprocStatement.IntoVariable, coprocStatement.IntoMode,
                         coprocStatement, (creates, createConst) => {
                           coprocStatement.IntoCreatesVariable = creates;
                           coprocStatement.IntoCreatesConst = createConst;
                         });
      break;

    case WaitStatement waitStatement:
      if (waitStatement.TargetKind == WaitTargetKind.Target &&
          waitStatement.Target != null)
        CheckExpression(waitStatement.Target);

      ResolveIntoBinding(waitStatement.IntoVariable, waitStatement.IntoMode,
                         waitStatement, (creates, createConst) => {
                           waitStatement.IntoCreatesVariable = creates;
                           waitStatement.IntoCreatesConst = createConst;
                         });
      break;

    case ShellCommandStatement shellCommand:
      ValidateShellCommand(shellCommand);
      break;

    case CommandStatement:
      break;

    case ShellStatement shellStatement:
      CheckExpression(shellStatement.Command);
      break;
    case TestStatement testStatement:
      CheckExpression(testStatement.Condition);
      break;
    case TrapStatement trapStatement:
      ValidateTrapSignal(trapStatement.Signal, trapStatement);
      if (trapStatement.Handler != null) {
        if (trapStatement.Handler.Arguments.Count != 0) {
          Report(trapStatement.Handler,
                 "Trap handler function calls cannot include arguments.",
                 DiagnosticCodes.InvalidTrapHandler);
        }

        ValidateFunctionCall(trapStatement.Handler, implicitArgs: 0);
      } else if (trapStatement.Command != null) {
        CheckExpression(trapStatement.Command);
      }
      break;
    case UntrapStatement untrapStatement:
      ValidateTrapSignal(untrapStatement.Signal, untrapStatement);
      break;

    case ExpressionStatement expressionStatement:
      CheckExpression(expressionStatement.Expression);
      break;
    }
  }

  private void CheckFunction(FunctionDeclaration function) {
    PushScope();
    functionDepth++;
    bool sawDefault = false;

    foreach (var parameter in function.Parameters) {
      if (IsBuiltinIdentifier(parameter.Name)) {
        Report(parameter,
               $"Cannot declare built-in variable '{parameter.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      if (parameter.DefaultValue == null) {
        if (sawDefault) {
          Report(
              parameter,
              $"Required parameter '{parameter.Name}' cannot appear after defaulted parameters.",
              DiagnosticCodes.InvalidParameterDeclaration);
        }
      } else {
        sawDefault = true;
        CheckExpression(parameter.DefaultValue);
      }

      Declare(parameter.Name, isConst: false, parameter);
    }

    foreach (var statement in function.Body)
      CheckStatement(statement);

    functionDepth--;
    PopScope();
  }

  private void CheckExpression(Expression expression) {
    switch (expression) {
    case IdentifierExpression identifier:
      ValidateIdentifierUse(identifier);
      break;

    case EnumAccessExpression enumAccess:
      ValidateEnumAccess(enumAccess);
      break;

    case FunctionCallExpression functionCall:
      ValidateFunctionCall(functionCall, implicitArgs: 0);
      break;

    case ShellCaptureExpression shellCapture:
      CheckExpression(shellCapture.Command);
      break;
    case TestCaptureExpression testCapture:
      CheckExpression(testCapture.Condition);
      break;

    case PipeExpression pipe:
      CheckExpression(pipe.Left);
      if (pipe.Right is IdentifierExpression target) {
        ValidateAssignmentTarget(target, isGlobal: false);
      } else if (pipe.Right is FunctionCallExpression call) {
        ValidateFunctionCall(call, implicitArgs: 1);
      } else {
        CheckExpression(pipe.Right);
      }
      break;

    case RedirectExpression redirect:
      CheckExpression(redirect.Left);
      CheckExpression(redirect.Right);
      break;

    case UnaryExpression unary:
      CheckExpression(unary.Operand);
      break;

    case BinaryExpression binary:
      CheckExpression(binary.Left);
      CheckExpression(binary.Right);
      break;

    case IndexAccessExpression indexAccess:
      CheckExpression(indexAccess.Array);
      CheckExpression(indexAccess.Index);
      break;

    case ArrayLiteral arrayLiteral:
      foreach (var element in arrayLiteral.Elements)
        CheckExpression(element);
      break;
    }
  }

  private void ValidateFunctionCall(FunctionCallExpression functionCall,
                                    int implicitArgs) {
    foreach (var argument in functionCall.Arguments)
      CheckExpression(argument);

    if (!functions.TryGetValue(functionCall.FunctionName,
                               out var functionInfo)) {
      Report(functionCall, $"Unknown function '{functionCall.FunctionName}'.",
             DiagnosticCodes.UnknownFunction);
      return;
    }

    var actual = functionCall.Arguments.Count + implicitArgs;
    if (actual < functionInfo.RequiredParameterCount ||
        actual > functionInfo.ParameterCount) {
      Report(
          functionCall,
          $"Function '{functionCall.FunctionName}' expects {FormatArity(functionInfo.RequiredParameterCount, functionInfo.ParameterCount)}, got {actual}.",
          DiagnosticCodes.FunctionArityMismatch);
    }
  }

  private void ValidateIdentifierUse(IdentifierExpression identifier) {
    if (IsBuiltinIdentifier(identifier.Name))
      return;

    if (TryResolveSymbol(identifier.Name, out _))
      return;

    Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
           DiagnosticCodes.UndeclaredVariable);
  }

  private void ValidateAssignmentTarget(IdentifierExpression identifier,
                                        bool isGlobal) {
    if (IsBuiltinIdentifier(identifier.Name)) {
      Report(identifier,
             $"Cannot assign to built-in variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
      return;
    }

    if (isGlobal) {
      if (!globalScope.TryGetValue(identifier.Name, out var symbol)) {
        Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
               DiagnosticCodes.UndeclaredVariable);
        return;
      }

      if (symbol.IsConst) {
        Report(identifier,
               $"Cannot assign to const variable '{identifier.Name}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      return;
    }

    if (!TryResolveSymbol(identifier.Name, out var resolved)) {
      Report(identifier, $"Use of undeclared variable '{identifier.Name}'.",
             DiagnosticCodes.UndeclaredVariable);
      return;
    }

    if (resolved.IsConst) {
      Report(identifier,
             $"Cannot assign to const variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
    }
  }

  private void
  ValidateIndexAssignmentTarget(IndexAccessExpression indexAccess) {
    CheckExpression(indexAccess.Array);
    CheckExpression(indexAccess.Index);

    if (indexAccess.Array is IdentifierExpression identifier &&
        IsBuiltinIdentifier(identifier.Name)) {
      Report(indexAccess,
             $"Cannot assign to built-in variable '{identifier.Name}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
    }
  }

  private void ValidateEnumAccess(EnumAccessExpression enumAccess) {
    if (!enums.TryGetValue(enumAccess.EnumName, out var members)) {
      Report(enumAccess, $"Unknown enum '{enumAccess.EnumName}'.",
             DiagnosticCodes.UndeclaredVariable);
      return;
    }

    if (!members.Contains(enumAccess.MemberName)) {
      Report(
          enumAccess,
          $"Unknown enum member '{enumAccess.EnumName}::{enumAccess.MemberName}'.",
          DiagnosticCodes.UndeclaredVariable);
    }
  }

  private void ValidateTrapSignal(string signal, AstNode node) {
    if (TryNormalizeTrapSignal(signal, out var normalized) &&
        ValidTrapSignals.Contains(normalized))
      return;

    Report(node, $"'{signal}' is not a valid trap signal.",
           DiagnosticCodes.InvalidTrapSignal);
  }

  private static bool TryNormalizeTrapSignal(string signal,
                                             out string normalized) {
    normalized = string.Empty;
    if (string.IsNullOrWhiteSpace(signal))
      return false;

    var trimmed = signal.Trim();
    if (trimmed.StartsWith("SIG", StringComparison.OrdinalIgnoreCase))
      trimmed = trimmed[3..];

    if (trimmed.Length == 0)
      return false;

    normalized = trimmed.ToUpperInvariant();
    return normalized.All(static c => c is >= 'A' and <= 'Z');
  }

  private void ValidateShellCommand(ShellCommandStatement command) {
    switch (command.Kind) {
    case ShellCommandKind.Set:
      ValidateSetCommand(command);
      break;
    case ShellCommandKind.Export:
      ValidateExportCommand(command);
      break;
    case ShellCommandKind.Shopt:
      ValidateShoptCommand(command);
      break;
    case ShellCommandKind.Alias:
      ValidateAliasCommand(command);
      break;
    case ShellCommandKind.Source:
      ValidateSourceCommand(command);
      break;
    }
  }

  private void ValidateSetCommand(ShellCommandStatement command) {
    var args = command.Arguments;
    for (var i = 0; i < args.Count; i++) {
      var arg = args[i];
      if (arg == "--")
        break;
      if (!TryParseOptionToken(arg, out var sign, out var optionBody))
        break;
      if (optionBody.Length == 0 || optionBody == "-" || optionBody == "+")
        continue;

      for (var flagIndex = 0; flagIndex < optionBody.Length; flagIndex++) {
        var flag = optionBody[flagIndex];
        if (flag == 'o') {
          if (flagIndex != optionBody.Length - 1) {
            Report(
                command,
                $"Invalid set option cluster '{arg}': 'o' must be the last short flag in the token.",
                DiagnosticCodes.InvalidCommandUsage);
            return;
          }

          if (i + 1 >= args.Count ||
              !ValidSetLongOptions.Contains(args[i + 1])) {
            Report(
                command,
                $"Invalid set option '{arg}': expected one of [{string.Join(", ", ValidSetLongOptions.OrderBy(v => v))}] after '{arg}'.",
                DiagnosticCodes.InvalidCommandUsage);
            return;
          }

          i++;
          continue;
        }

        if (!ValidSetShortFlags.Contains(flag)) {
          Report(command, $"Invalid set flag '{sign}{flag}'.",
                 DiagnosticCodes.InvalidCommandUsage);
          return;
        }
      }
    }
  }

  private void ValidateExportCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidExportShortFlags, "export",
                                ValidateExportArgument);
  }

  private void ValidateShoptCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidShoptShortFlags, "shopt",
                                ValidateShoptArgument);
  }

  private void ValidateAliasCommand(ShellCommandStatement command) {
    ValidateSimpleOptionCommand(command, ValidAliasShortFlags, "alias",
                                ValidateAliasArgument);
  }

  private void ValidateSourceCommand(ShellCommandStatement command) {
    if (command.Arguments.Count == 0) {
      Report(command, "Command 'source' requires a path argument.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateSimpleOptionCommand(
      ShellCommandStatement command, HashSet<char> allowedFlags,
      string commandName,
      Action<ShellCommandStatement, string> validateNonOptionArgument) {
    var stopOptions = false;
    foreach (var arg in command.Arguments) {
      if (!stopOptions && arg == "--") {
        stopOptions = true;
        continue;
      }

      if (!stopOptions &&
          TryParseOptionToken(arg, out var sign, out var optionBody)) {
        if (sign != '-' || optionBody.Length == 0) {
          Report(command, $"Invalid {commandName} option '{arg}'.",
                 DiagnosticCodes.InvalidCommandUsage);
          return;
        }

        foreach (var flag in optionBody) {
          if (!allowedFlags.Contains(flag)) {
            Report(command, $"Invalid {commandName} flag '-{flag}'.",
                   DiagnosticCodes.InvalidCommandUsage);
            return;
          }
        }

        continue;
      }

      validateNonOptionArgument(command, arg);
    }
  }

  private void ValidateExportArgument(ShellCommandStatement command,
                                      string arg) {
    if (TrySplitAssignment(arg, out var name)) {
      if (!IsIdentifier(name)) {
        Report(command, $"Invalid export assignment target '{name}'.",
               DiagnosticCodes.InvalidCommandUsage);
      }

      return;
    }

    if (!IsIdentifier(arg)) {
      Report(command, $"Invalid export target '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateShoptArgument(ShellCommandStatement command,
                                     string arg) {
    if (arg.Length == 0) {
      Report(command, "Invalid shopt argument.",
             DiagnosticCodes.InvalidCommandUsage);
      return;
    }

    if (!arg.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) {
      Report(command, $"Invalid shopt option name '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private void ValidateAliasArgument(ShellCommandStatement command,
                                     string arg) {
    if (TrySplitAssignment(arg, out var aliasName)) {
      if (!IsIdentifier(aliasName)) {
        Report(command, $"Invalid alias name '{aliasName}'.",
               DiagnosticCodes.InvalidCommandUsage);
      }

      return;
    }

    if (!IsIdentifier(arg)) {
      Report(command, $"Invalid alias name '{arg}'.",
             DiagnosticCodes.InvalidCommandUsage);
    }
  }

  private static bool TrySplitAssignment(string text, out string name) {
    name = string.Empty;
    var equals = text.IndexOf('=');
    if (equals <= 0)
      return false;

    name = text[..equals];
    return true;
  }

  private static bool TryParseOptionToken(string token, out char sign,
                                          out string optionBody) {
    sign = '\0';
    optionBody = string.Empty;

    if (token.Length < 2)
      return false;

    var first = token[0];
    if (first is not('-' or '+'))
      return false;

    if (token == "--") {
      sign = '-';
      optionBody = "-";
      return true;
    }

    sign = first;
    optionBody = token[1..];
    return true;
  }

  private static bool IsIdentifier(string value) {
    if (string.IsNullOrWhiteSpace(value))
      return false;
    if (!(char.IsLetter(value[0]) || value[0] == '_'))
      return false;
    for (var i = 1; i < value.Length; i++) {
      var ch = value[i];
      if (!(char.IsLetterOrDigit(ch) || ch == '_'))
        return false;
    }

    return true;
  }

  private void ResolveIntoBinding(string? targetName, IntoBindingMode mode,
                                  AstNode node,
                                  Action<bool, bool> setResolution) {
    setResolution(false, false);
    if (string.IsNullOrEmpty(targetName))
      return;

    if (IsBuiltinIdentifier(targetName)) {
      Report(node, $"Cannot assign to built-in variable '{targetName}'.",
             DiagnosticCodes.InvalidAssignmentTarget);
      return;
    }

    if (TryResolveSymbol(targetName, out var resolved)) {
      if (mode is IntoBindingMode.Let or IntoBindingMode.Const) {
        // Declaration forms always declare in the current scope.
        var createConstFromMode = mode == IntoBindingMode.Const;
        Declare(targetName, createConstFromMode, node, isGlobal: false);
        setResolution(true, createConstFromMode);
        return;
      }

      if (resolved.IsConst) {
        Report(node, $"Cannot assign to const variable '{targetName}'.",
               DiagnosticCodes.InvalidAssignmentTarget);
      }

      return;
    }

    if (mode == IntoBindingMode.Assign) {
      Report(node, $"Cannot assign to undeclared variable '{targetName}'.",
             DiagnosticCodes.UndeclaredVariable);
      return;
    }

    var createConst = mode == IntoBindingMode.Const;
    Declare(targetName, createConst, node, isGlobal: false);
    setResolution(true, createConst);
  }

  private void PushScope() {
    scopes.Push(new Dictionary<string, SymbolInfo>(scopes.Peek(),
                                                   StringComparer.Ordinal));
    declaredInScope.Push(new HashSet<string>(StringComparer.Ordinal));
  }

  private void PopScope() {
    scopes.Pop();
    declaredInScope.Pop();
  }

  private void Declare(string name, bool isConst, AstNode node,
                       bool isGlobal = false) {
    if (isGlobal) {
      if (!globalDeclared.Add(name)) {
        Report(node, $"Duplicate declaration of '{name}' in the same scope.",
               DiagnosticCodes.DuplicateDeclaration);
        return;
      }

      globalScope[name] = new SymbolInfo(isConst);
      return;
    }

    if (!declaredInScope.Peek().Add(name)) {
      Report(node, $"Duplicate declaration of '{name}' in the same scope.",
             DiagnosticCodes.DuplicateDeclaration);
      return;
    }

    scopes.Peek()[name] = new SymbolInfo(isConst);
  }

  private bool TryResolveSymbol(string name, out SymbolInfo symbol) {
    foreach (var scope in scopes) {
      if (scope.TryGetValue(name, out symbol))
        return true;
    }

    symbol = default;
    return false;
  }

  private void Report(AstNode node, string message, string code) {
    diagnostics.AddError(WithTip(message, code), node.Line, node.Column, code);
  }

  private static string WithTip(string message, string code) {
    var tip = code switch {
      DiagnosticCodes.InvalidAssignmentTarget =>
          "Use 'let' for mutable variables, or remove this assignment.",
      DiagnosticCodes.UndeclaredVariable =>
          "Declare the symbol before first use with 'let' or 'const'.",
      DiagnosticCodes.UnknownFunction =>
          "Declare the function before calling it, or fix the function name.",
      DiagnosticCodes.FunctionArityMismatch =>
          "Match the call arity to the function signature (required + " +
          "optional parameters).",
      DiagnosticCodes.InvalidControlFlowContext =>
          "Move this statement into a valid context (loop or function).",
      DiagnosticCodes.InvalidParameterDeclaration =>
          "Place required parameters before defaulted parameters.",
      DiagnosticCodes.DuplicateDeclaration =>
          "Rename the symbol or remove the duplicate declaration in this " +
          "scope.",
      DiagnosticCodes.InvalidCommandUsage =>
          "Use a valid form for this command, or use sh to emit it directly.",
      _ => null
    };

    return DiagnosticMessage.WithTip(message, tip);
  }

  private static string FormatArity(int required, int total) {
    if (required == total)
      return total.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return $"{required}..{total}";
  }

  private static bool IsBuiltinIdentifier(string name) =>
      string.Equals(name, "argv", StringComparison.Ordinal);

  private readonly record struct SymbolInfo(bool IsConst);
  private readonly record struct FunctionInfo(int ParameterCount,
                                              int RequiredParameterCount);
}
