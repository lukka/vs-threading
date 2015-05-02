﻿namespace Microsoft.VisualStudio.ProjectSystem.SDK.Analyzer
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Report warnings when detect the code that is waiting on tasks or awaiters synchronously.
    /// </summary>
    /// <remarks>
    /// [Background] <see cref="Task.Wait"/> or <see cref="Task{TResult}.Result"/> will often deadlock if
    /// they are called on main thread, because now it is synchronously blocking the main thread for the
    /// completion of a task that may need the main thread to complete. Even if they are called on a threadpool
    /// thread, it is occupying a threadpool thread to do nothing but block, which is not good either.
    /// 
    /// i.e.
    ///   var task = Task.Run(DoSomethingOnBackground);
    ///   task.Wait();  /* This analyzer will report warning on this synchronous wait. */
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SynchronousWaitAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(Rules.SynchronousWaitRule);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this.AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(this.AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var node = (InvocationExpressionSyntax)context.Node;
            var invokeMethod = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IMethodSymbol;
            if (invokeMethod != null)
            {
                var taskType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName);
                if (String.Equals(invokeMethod.Name, nameof(Task.Wait), StringComparison.Ordinal)
                    && Utils.IsEqualToOrDerivedFrom(invokeMethod.ContainingType, taskType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SynchronousWaitRule, context.Node.GetLocation()));
                }
                else if (String.Equals(invokeMethod.Name, "GetResult", StringComparison.Ordinal)
                    && invokeMethod.ContainingType.Name.EndsWith("Awaiter", StringComparison.Ordinal))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SynchronousWaitRule, context.Node.GetLocation()));
                }
            }
        }

        private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var property = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IPropertySymbol;
            if (property != null)
            {
                var taskType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName);
                if (String.Equals(property.Name, nameof(Task<Object>.Result), StringComparison.Ordinal)
                    && Utils.IsEqualToOrDerivedFrom(property.ContainingType, taskType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SynchronousWaitRule, context.Node.GetLocation()));
                }
            }
        }
    }
}