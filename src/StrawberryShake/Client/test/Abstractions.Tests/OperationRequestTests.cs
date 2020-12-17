using System.Collections.Generic;
using Moq;
using Xunit;

namespace StrawberryShake.Abstractions
{
    public class OperationRequestTests
    {
        [Fact]
        public void Equals_With_Variables()
        {
            // arrange
            var document = new Mock<IDocument>();

            var a = new OperationRequest(
                null,
                "abc",
                document.Object,
                new Dictionary<string, object?>{ { "a", "a" } });

            var b = new OperationRequest(
                null,
                "abc",
                document.Object,
                new Dictionary<string, object?>{ { "a", "a" } });

            // act
            // assert
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_No_Variables()
        {
            // arrange
            var document = new Mock<IDocument>();

            var a = new OperationRequest(
                null,
                "abc",
                document.Object);

            var b = new OperationRequest(
                null,
                "abc",
                document.Object);

            // act
            // assert
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void GetHashCode_With_Variables()
        {
            // arrange
            var document = new Mock<IDocument>();

            var a = new OperationRequest(
                null,
                "abc",
                document.Object,
                new Dictionary<string, object?>{ { "a", "a" } });

            var b = new OperationRequest(
                null,
                "abc",
                document.Object,
                new Dictionary<string, object?>{ { "a", "a" } });

            // act
            var hashCodeA = a.GetHashCode();
            var hashCodeB = b.GetHashCode();

            // assert
            Assert.Equal(hashCodeA, hashCodeB);
        }

        [Fact]
        public void GetHashCode_No_Variables()
        {
            // arrange
            var document = new Mock<IDocument>();

            var a = new OperationRequest(
                null,
                "abc",
                document.Object);

            var b = new OperationRequest(
                null,
                "abc",
                document.Object);

            // act
            var hashCodeA = a.GetHashCode();
            var hashCodeB = b.GetHashCode();

            // assert
            Assert.Equal(hashCodeA, hashCodeB);
        }

        [Fact]
        public void Deconstruct()
        {
            // arrange
            var document = new Mock<IDocument>();

            var request = new OperationRequest(
                null,
                "abc",
                document.Object);

            // act
            string? id;
            string name;
            IDocument doc;
            IReadOnlyDictionary<string, object?> vars;
            IReadOnlyDictionary<string, object?>? ext;
            IReadOnlyDictionary<string, object?>? contextData;
            (id, name, doc, vars, ext, contextData) = request;

            // assert
            Assert.Equal(request.Id, id);
            Assert.Equal(request.Name, name);
            Assert.Equal(request.Document, doc);
            Assert.Equal(request.Variables, vars);
            Assert.Null(ext);
            Assert.Null(contextData);
        }
    }
}