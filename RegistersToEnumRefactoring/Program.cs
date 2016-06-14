using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cosmos.Assembler.x86;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTest
{
    class Program
    {
        private static MSBuildWorkspace mWorkspace;
        private static Solution mSolution;
        private static INamedTypeSymbol mRegistersClassDeclaration;
        private static INamedTypeSymbol mEnumDeclaration;

        static void Main(string[] args)
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                Task.WaitAll(DoMainAsync());
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine(e.ToString());
            }
        }

        private static async Task DoMainAsync()
        {
            var xCount = 0;
            bool xSomethingChanged = true;

            using (mWorkspace = MSBuildWorkspace.Create())
            {
                mWorkspace.LoadMetadataForReferencedProjects = true;
                mSolution = await mWorkspace.OpenSolutionAsync(@"C:\Data\Sources\OpenSource\Cosmos\source\Build.sln");
                while (xSomethingChanged)
                {
                    await FindBaseTypesAsync();

                    var xReferences = (await SymbolFinder.FindReferencesAsync(mRegistersClassDeclaration, mSolution)).ToArray();
                    foreach (var xItem in xReferences)
                    {
                        bool xShouldBreak = false;
                        foreach (var xLocation in xItem.Locations)
                        {
                            var xDocument = mSolution.GetDocument(xLocation.Document.Id);
                            var xSemMod = await xDocument.GetSemanticModelAsync();

                            //var xSymbol = xSemMod.GetEnclosingSymbol(xLocation.Location.SourceSpan.Start);
                            var xSyntaxRoot = await xDocument.GetSyntaxRootAsync();

                            //var xSyntaxTree = await xDocument.GetSyntaxTreeAsync();
                            var xClassNameToken = xSyntaxRoot.FindToken(xLocation.Location.SourceSpan.Start);
                            var xAccess = TryGetMemberAccessExpressionParent(xClassNameToken.Parent);
                            if (xAccess == null)
                            {
                                continue;
                            }
                            xCount++;



                            var xEditor = await DocumentEditor.CreateAsync(xDocument);

                            var xNewAccessExpression = RewriteExpression(xEditor.Generator, xAccess);
                            xEditor.ReplaceNode(xAccess, xNewAccessExpression);

                            mSolution = mSolution.WithDocumentSyntaxRoot(xDocument.Id, xEditor.GetChangedRoot());
                            xSomethingChanged = mWorkspace.TryApplyChanges(mSolution);
                            await FindBaseTypesAsync();
                            mSolution = mWorkspace.CurrentSolution;
                            if (xSomethingChanged)
                            {
                                Console.Write(".");
                            }
                            else
                            {
                                Console.Write("!");
                            }
                            //xShouldBreak = true;

                            //break;
                        }
                        if (xShouldBreak)
                        {
                            //break;
                        }
                    }
                }
            }
            Console.WriteLine("Registers is being used {0} times", xCount);
        }

        private static async Task FindBaseTypesAsync()
        {
            mRegistersClassDeclaration = null;
            mEnumDeclaration = null;
            var xEnums = new Dictionary<string, ISymbol>();

            var xAsmProj = mSolution.Projects.Single(i => i.Name == "Cosmos.Assembler");
            var xAsmProjCompilation = await xAsmProj.GetCompilationAsync();
            mRegistersClassDeclaration = xAsmProjCompilation.GetTypeByMetadataName(typeof(Registers).FullName);

            mEnumDeclaration = xAsmProjCompilation.GetTypeByMetadataName(typeof(RegistersEnum).FullName);

            foreach (var xMember in mEnumDeclaration.GetMembers())
            {
                xEnums.Add(xMember.Name, xMember);
            }
        }

        private static ExpressionSyntax RewriteExpression(SyntaxGenerator generator, MemberAccessExpressionSyntax expression)
        {
            var xMemberAccess = expression.Expression as MemberAccessExpressionSyntax;
            if (xMemberAccess != null)
            {
                var xParentToUse = xMemberAccess.Expression;
                return (ExpressionSyntax)generator.MemberAccessExpression(generator.MemberAccessExpression(xParentToUse, nameof(RegistersEnum)), expression.Name);
            }
            return (ExpressionSyntax)generator.MemberAccessExpression(generator.IdentifierName(nameof(RegistersEnum)), expression.Name);
        }

        private static MemberAccessExpressionSyntax TryGetMemberAccessExpressionParent(SyntaxNode token)
        {
            var xCurrentToken = token;
            while (xCurrentToken != null)
            {
                var xToken = xCurrentToken as MemberAccessExpressionSyntax;
                if (xToken != null)
                {
                    var xRegisterValue = Registers.GetRegister(xToken.Name.ToString());
                    if (xRegisterValue != null)
                    {
                        return xToken;
                    }
                }

                xCurrentToken = xCurrentToken.Parent;
            }
            return null;
        }
    }
}
