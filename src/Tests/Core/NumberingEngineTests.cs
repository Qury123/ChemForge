using System;
using System.Linq;
using ChemForge.Core.Numbering;
using ChemForge.Models.Numbering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Core
{
    /// <summary>
    /// NumberingEngine 单元测试
    /// </summary>
    [TestClass]
    public sealed class NumberingEngineTests
    {
        [TestMethod]
        public void GenerateNumbers_WithZeroIncrement_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var engine = new NumberingEngine();
            var config = new NumberingConfig { Increment = 0 };

            // Act
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => engine.GenerateNumbers(config, 3).ToList());

            // Assert
            Assert.AreEqual("config.Increment", ex.ParamName);
        }

        [TestMethod]
        public void GenerateNumbers_WithZeroDigits_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var engine = new NumberingEngine();
            var config = new NumberingConfig { Digits = 0 };

            // Act
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => engine.GenerateNumbers(config, 3).ToList());

            // Assert
            Assert.AreEqual("config.Digits", ex.ParamName);
        }

        [TestMethod]
        public void GenerateNumbers_WithNegativeDigits_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var engine = new NumberingEngine();
            var config = new NumberingConfig { Digits = -1 };

            // Act
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => engine.GenerateNumbers(config, 3).ToList());

            // Assert
            Assert.AreEqual("config.Digits", ex.ParamName);
        }

        [TestMethod]
        public void GenerateNumbers_WithNonPositiveCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var engine = new NumberingEngine();
            var config = new NumberingConfig();

            // Act
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => engine.GenerateNumbers(config, 0).ToList());

            // Assert
            Assert.AreEqual("count", ex.ParamName);
        }

        [TestMethod]
        public void GenerateNumbers_WithValidConfig_ReturnsFormattedSequence()
        {
            // Arrange
            var engine = new NumberingEngine();
            var config = new NumberingConfig
            {
                Prefix = "P-",
                Suffix = "-A",
                StartNumber = 1,
                Increment = 2,
                Digits = 3
            };

            // Act
            var result = engine.GenerateNumbers(config, 3).ToArray();

            // Assert
            CollectionAssert.AreEqual(
                new[] { "P-001-A", "P-003-A", "P-005-A" },
                result);
        }
    }
}
