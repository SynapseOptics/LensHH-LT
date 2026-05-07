using System;
using System.IO;
using System.Linq;
using LensHH.API;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;
using Xunit;

namespace LensHH.API.Tests
{
    public class SessionLifecycleTests
    {
        [Fact]
        public void NewSystem_CreatesValidSystem()
        {
            var session = new LensHHSession();
            session.NewSystem();

            Assert.True(session.HasSystem);
            Assert.Equal(3, session.System.Surfaces.Count); // object + stop + image
            Assert.Single(session.System.Wavelengths);
            Assert.Single(session.System.Fields);
            Assert.Equal("New System", session.System.Title);
        }

        [Fact]
        public void NewSystem_StopSurfaceIsSet()
        {
            var session = new LensHHSession();
            session.NewSystem();

            Assert.Equal(1, session.System.StopSurfaceIndex);
        }

        [Fact]
        public void HasSystem_FalseBeforeLoad()
        {
            var session = new LensHHSession();
            Assert.False(session.HasSystem);
        }

        [Fact]
        public void System_ThrowsWhenNotLoaded()
        {
            var session = new LensHHSession();
            Assert.Throws<InvalidOperationException>(() => session.System);
        }
    }

    public class SystemEditorTests
    {
        private LensHHSession CreateSession()
        {
            var s = new LensHHSession();
            s.NewSystem();
            return s;
        }

        [Fact]
        public void SetTitle_UpdatesTitle()
        {
            var session = CreateSession();
            session.SetTitle("Test Lens");
            Assert.Equal("Test Lens", session.System.Title);
        }

        [Fact]
        public void SetAperture_EPD()
        {
            var session = CreateSession();
            session.SetAperture(ApertureType.EPD, 25.0);
            Assert.Equal(ApertureType.EPD, session.System.Aperture.Type);
            Assert.Equal(25.0, session.System.Aperture.Value);
        }

        [Fact]
        public void AddSurface_InsertsBeforeImage()
        {
            var session = CreateSession();
            int initialCount = session.System.Surfaces.Count;

            int idx = session.AddSurface(radius: 100, thickness: 5, material: "BK7");

            Assert.Equal(initialCount + 1, session.System.Surfaces.Count);
            Assert.Equal(100, session.System.Surfaces[idx].Radius);
            Assert.Equal("BK7", session.System.Surfaces[idx].Material);
        }

        [Fact]
        public void RemoveSurface_RemovesCorrectSurface()
        {
            var session = CreateSession();
            session.AddSurface(radius: 50, thickness: 3, material: "SF5");
            int countBefore = session.System.Surfaces.Count;

            session.RemoveSurface(2); // remove the added surface

            Assert.Equal(countBefore - 1, session.System.Surfaces.Count);
        }

        [Fact]
        public void RemoveSurface_CannotRemoveObjectOrImage()
        {
            var session = CreateSession();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RemoveSurface(0)); // object
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                session.RemoveSurface(session.System.Surfaces.Count - 1)); // image
        }

        [Fact]
        public void SetWavelengths_ReplaceAll()
        {
            var session = CreateSession();
            session.SetWavelengths(new[] { 0.486, 0.587, 0.656 }, primaryIndex: 1);

            Assert.Equal(3, session.System.Wavelengths.Count);
            Assert.True(session.System.Wavelengths[1].IsPrimary);
            Assert.Equal(0.587, session.System.Wavelengths[1].Value, 3);
        }

        [Fact]
        public void SetFields_ReplaceAll()
        {
            var session = CreateSession();
            session.SetFields(new[] { 0.0, 10.0, 20.0 });

            Assert.Equal(3, session.System.Fields.Count);
            Assert.Equal(20.0, session.System.Fields[2].Y);
        }

        [Fact]
        public void SetCurvatureVariable_SetsFlag()
        {
            var session = CreateSession();
            session.SetCurvatureVariable(1, true, min: 0.01, max: 0.1);

            Assert.True(session.System.Surfaces[1].CurvatureVariable);
            Assert.Equal(0.01, session.System.Surfaces[1].CurvatureMin);
            Assert.Equal(0.1, session.System.Surfaces[1].CurvatureMax);
        }

        [Fact]
        public void ClearAllVariables_ResetsEverything()
        {
            var session = CreateSession();
            session.SetCurvatureVariable(1, true);
            session.SetThicknessVariable(1, true);

            session.ClearAllVariables();

            Assert.False(session.System.Surfaces[1].CurvatureVariable);
            Assert.False(session.System.Surfaces[1].ThicknessVariable);
        }
    }

    public class MeritFunctionTests
    {
        [Fact]
        public void NewMeritFunction_CreatesEmpty()
        {
            var session = new LensHHSession();
            session.NewSystem();
            session.NewMeritFunction();

            Assert.NotNull(session.MeritFunction);
            Assert.Empty(session.MeritFunction.Operands);
        }

        [Fact]
        public void AddOperand_AddsToMeritFunction()
        {
            var session = new LensHHSession();
            session.NewSystem();
            session.NewMeritFunction();
            session.AddOperand(OperandType.EFL, target: 100, weight: 1.0);

            Assert.Single(session.MeritFunction!.Operands);
            Assert.Equal(OperandType.EFL, session.MeritFunction.Operands[0].Type);
            Assert.Equal(100, session.MeritFunction.Operands[0].Target);
        }

        [Fact]
        public void ClearMeritFunction_RemovesAll()
        {
            var session = new LensHHSession();
            session.NewSystem();
            session.NewMeritFunction();
            session.AddOperand(OperandType.EFL, target: 100);

            session.ClearMeritFunction();

            Assert.Null(session.MeritFunction);
        }
    }

    public class FileIOTests
    {
        [Fact]
        public void SaveAndLoad_RoundTrip()
        {
            var session = new LensHHSession();
            session.NewSystem();
            session.SetTitle("Round Trip Test");
            session.AddSurface(radius: 50, thickness: 5, material: "BK7");

            string path = Path.GetTempFileName() + ".lhlt";
            try
            {
                session.SaveAs(path);
                Assert.True(File.Exists(path));

                var session2 = new LensHHSession();
                session2.Load(path);
                Assert.Equal("Round Trip Test", session2.System.Title);
                Assert.Equal(4, session2.System.Surfaces.Count); // obj + stop + added + image
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Save_ThrowsWithoutPath()
        {
            var session = new LensHHSession();
            session.NewSystem();
            Assert.Throws<InvalidOperationException>(() => session.Save());
        }
    }

    public class InterfaceTests
    {
        [Fact]
        public void Session_ImplementsAllInterfaces()
        {
            var session = new LensHHSession();

            Assert.IsAssignableFrom<IFileIO>(session);
            Assert.IsAssignableFrom<ISystemEditor>(session);
            Assert.IsAssignableFrom<IAnalysis>(session);
            Assert.IsAssignableFrom<IOptimization>(session);
            Assert.IsAssignableFrom<IRendering>(session);
            Assert.IsAssignableFrom<ITextExport>(session);
        }

        [Fact]
        public void IAnalysis_CanBeUsedIndependently()
        {
            var session = new LensHHSession();
            session.NewSystem();

            IAnalysis analysis = session;
            var parax = analysis.ParaxialData();
            // New system with no glass has no optical power — EFL may be infinity or NaN
            Assert.NotNull(parax);
        }
    }
}
