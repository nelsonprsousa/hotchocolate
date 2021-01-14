using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using HotChocolate.StarWars;
using HotChocolate.Execution;
using HotChocolate.Language;
using Xunit;

namespace StrawberryShake.CodeGeneration.Analyzers
{
    public class InterfaceTypeSelectionSetAnalyzerTests
    {
        [Fact]
        public async Task Interface_With_Default_Names_One_Models()
        {
            // arrange
            var schema =
                await new ServiceCollection()
                    .AddStarWarsRepositories()
                    .AddGraphQL()
                    .AddStarWars()
                    .BuildSchemaAsync();

            var document =
                Utf8GraphQLParser.Parse(@"
                    query GetHero {
                        hero(episode: NEW_HOPE) {
                            name
                        }
                    }");

            var context = new DocumentAnalyzerContext(schema, document);
            SelectionSetVariants selectionSetVariants = context.CollectFields();
            FieldSelection fieldSelection = selectionSetVariants.ReturnType.Fields.First();
            selectionSetVariants = context.CollectFields(fieldSelection);

            // act
            var analyzer = new InterfaceTypeSelectionSetAnalyzer();
            var result = analyzer.Analyze(context, fieldSelection, selectionSetVariants);

            // assert
            Assert.Equal("IGetHero_Hero", result.Name);

            Assert.Collection(
                context.GetImplementations(result),
                model => Assert.Equal("GetHero_Hero", model.Name));

            Assert.Collection(
                result.Fields,
                field => Assert.Equal("name", field.Name));
        }

        [Fact]
        public async Task Interface_With_Default_Names_Two_Models()
        {
            // arrange
            var schema =
                await new ServiceCollection()
                    .AddStarWarsRepositories()
                    .AddGraphQL()
                    .AddStarWars()
                    .BuildSchemaAsync();

            var document =
                Utf8GraphQLParser.Parse(@"
                    query GetHero {
                        hero(episode: NEW_HOPE) {
                            name
                            ... on Droid {
                                primaryFunction
                            }
                        }
                    }");

            var context = new DocumentAnalyzerContext(schema, document);
            SelectionSetVariants selectionSetVariants = context.CollectFields();
            FieldSelection fieldSelection = selectionSetVariants.ReturnType.Fields.First();
            selectionSetVariants = context.CollectFields(fieldSelection);

            // act
            var analyzer = new InterfaceTypeSelectionSetAnalyzer();
            var result = analyzer.Analyze(context, fieldSelection, selectionSetVariants);

            // assert
            Assert.Equal("IGetHero_Hero", result.Name);

            Assert.Collection(
                context.GetImplementations(result),
                model => Assert.Equal("GetHero_Hero_Human", model.Name),
                model => Assert.Equal("GetHero_Hero_Droid", model.Name));

            Assert.Collection(
                result.Fields,
                field => Assert.Equal("name", field.Name));
        }

        [Fact]
        public async Task Interface_With_Fragment_Definition_One_Model()
        {
            // arrange
            var schema =
                await new ServiceCollection()
                    .AddStarWarsRepositories()
                    .AddGraphQL()
                    .AddStarWars()
                    .BuildSchemaAsync();

            var document =
                Utf8GraphQLParser.Parse(@"
                    query GetHero {
                        hero(episode: NEW_HOPE) {
                            ... Hero
                        }
                    }

                    fragment Hero on Character {
                        name
                    }");

            var context = new DocumentAnalyzerContext(schema, document);
            SelectionSetVariants selectionSetVariants = context.CollectFields();
            FieldSelection fieldSelection = selectionSetVariants.ReturnType.Fields.First();
            selectionSetVariants = context.CollectFields(fieldSelection);

            // act
            var analyzer = new InterfaceTypeSelectionSetAnalyzer();
            var result = analyzer.Analyze(context, fieldSelection, selectionSetVariants);

            // assert
            Assert.Equal("IHero", result.Name);

            Assert.Collection(
                context.GetImplementations(result),
                model => Assert.Equal("Hero", model.Name));

            Assert.Collection(
                result.Fields,
                field => Assert.Equal("name", field.Name));
        }

        [Fact]
        public async Task Interface_With_Fragment_Definition_Two_Models()
        {
            // arrange
            var schema =
                await new ServiceCollection()
                    .AddStarWarsRepositories()
                    .AddGraphQL()
                    .AddStarWars()
                    .BuildSchemaAsync();

            var document =
                Utf8GraphQLParser.Parse(@"
                    query GetHero {
                        hero(episode: NEW_HOPE) {
                            ... Hero
                        }
                    }

                    fragment Hero on Character {
                        name
                        ... Human
                        ... Droid
                    }

                    fragment Human on Human {
                        homePlanet
                    }

                    fragment Droid on Droid {
                        primaryFunction
                    }");

            var context = new DocumentAnalyzerContext(schema, document);
            SelectionSetVariants selectionSetVariants = context.CollectFields();
            FieldSelection fieldSelection = selectionSetVariants.ReturnType.Fields.First();
            selectionSetVariants = context.CollectFields(fieldSelection);

            // act
            var analyzer = new InterfaceTypeSelectionSetAnalyzer();
            var result = analyzer.Analyze(context, fieldSelection, selectionSetVariants);

            // assert
            Assert.Equal("IHero", result.Name);

            Assert.Collection(
                context.GetImplementations(result),
                model => Assert.Equal("Human", model.Name),
                model => Assert.Equal("Droid", model.Name));

            Assert.Collection(
                result.Fields,
                field => Assert.Equal("name", field.Name));
        }
    }
}