﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using R5T.NetStandard;
using R5T.NetStandard.IO.Serialization;

using R5T.Code.VisualStudio.Model;
using R5T.Code.VisualStudio.Model.SolutionFileSpecific;

using SolutionUtilities = R5T.Code.VisualStudio.Model.SolutionFileSpecific.Utilities;


namespace R5T.Code.VisualStudio.IO
{
    public class SolutionFileTextSerializer : ITextSerializer<SolutionFile>
    {
        public const string ProjectLineRegexPattern = @"^Project";
        public const string ProjectLineEndRegexPattern = @"^EndProject";
        public const string GlobalLineRegexPattern = @"^Global";
        public const string GlobalEndLineRegexPattern = @"^EndGlobal($|\s)";
        public const string GlobalSectionRegexPattern = @"^GlobalSection";
        public const string GlobalSectionEndRegexPattern = @"^EndGlobalSection";

        public const string ProjectLineValuesRegexPattern = @"""([^""]*)"""; // Find matches in quotes, but includes the quotes.
        public const string GlobalSectionLineValuesRegexPattern = @"\(.*\)|(?<== ).*";


        #region Static

        private static Regex ProjectLineRegex { get; } = new Regex(SolutionFileTextSerializer.ProjectLineRegexPattern);
        private static Regex ProjectLineEndRegex { get; } = new Regex(SolutionFileTextSerializer.ProjectLineEndRegexPattern);
        private static Regex GlobalLineRegex { get; } = new Regex(SolutionFileTextSerializer.GlobalLineRegexPattern);
        private static Regex GlobalLineEndRegex { get; } = new Regex(SolutionFileTextSerializer.GlobalEndLineRegexPattern);
        private static Regex GlobalSectionRegex { get; } = new Regex(SolutionFileTextSerializer.GlobalSectionRegexPattern);
        private static Regex GlobalSectionEndRegex { get; } = new Regex(SolutionFileTextSerializer.GlobalSectionEndRegexPattern);


        private static void SerializeGlobals(TextWriter writer, SolutionFile solutionFile)
        {
            var tabinatedWriter = new TabinatedWriter(writer);

            tabinatedWriter.WriteLine("Global");

            tabinatedWriter.IncreaseTabination();

            var globalSections = new List<ISolutionFileGlobalSection>(solutionFile.GlobalSections);
            var globalSectionsInOrder = new List<ISolutionFileGlobalSection>();

            // SolutionConfigurationPlatforms.
            var solutionConfigurationPlatforms = globalSections.GetSolutionConfigurationPlatformsGlobalSection(); // Must have.

            globalSectionsInOrder.Add(solutionConfigurationPlatforms);
            globalSections.Remove(solutionConfigurationPlatforms);

            // ProjectConfigurationPlatforms.
            var hasProjectConfigurationPlatforms = globalSections.HasProjectConfigurationPlatformsGlobalSection(out var projectConfigurationPlatforms); // Can have.
            if(hasProjectConfigurationPlatforms)
            {
                globalSectionsInOrder.Add(projectConfigurationPlatforms);
                globalSections.Remove(projectConfigurationPlatforms);
            }

            // Solution properties.
            var solutionProperties = globalSections.GetGlobalSectionByName<GeneralSolutionFileGlobalSection>(Constants.SolutionPropertiesSolutionGlobalSectionName); // Must have.

            globalSectionsInOrder.Add(solutionProperties);
            globalSections.Remove(solutionProperties);

            // Nested projects.
            var hasNestedProjects = globalSections.HasNestedProjectsGlobalSection(out var nestedProjects); // Can have.
            if (hasNestedProjects)
            {
                globalSectionsInOrder.Add(nestedProjects);
                globalSections.Remove(nestedProjects);
            }

            // Extensibility globals.
            var hasExtensibilityGlobals = globalSections.HasGlobalSectionByName<GeneralSolutionFileGlobalSection>(Constants.ExtensibilityGlobalsSolutionGlobalSectionName, out var extensibilityGlobals); // Can have.
            if(hasExtensibilityGlobals)
            {
                globalSectionsInOrder.Add(extensibilityGlobals);
                globalSections.Remove(extensibilityGlobals);
            }

            // All others that remain.
            globalSectionsInOrder.AddRange(globalSections);

            foreach (var globalSection in globalSectionsInOrder)
            {
                if(globalSection is ISerializableSolutionFileGlobalSection serializableGlobalSection)
                {
                    SolutionFileTextSerializer.SerializeGlobal(tabinatedWriter, serializableGlobalSection);
                }
                else
                {
                    throw new IOException($"Unable to serialize solution file global section type: {globalSection.GetType().FullName}");
                }
            }
            tabinatedWriter.DecreaseTabination();

            tabinatedWriter.WriteLine("EndGlobal");
        }

        private static void SerializeGlobal(TabinatedWriter writer, ISerializableSolutionFileGlobalSection serializableGlobalSection)
        {
            var globalSectionLine = $"GlobalSection({serializableGlobalSection.Name}) = {SolutionUtilities.ToStringStandard(serializableGlobalSection.PreOrPostSolution)}";
            writer.WriteLine(globalSectionLine);

            writer.IncreaseTabination();
            foreach (var line in serializableGlobalSection.ContentLines)
            {
                writer.WriteLine(line);
            }
            writer.DecreaseTabination();

            writer.WriteLine("EndGlobalSection");
        }

        private static void SerializeProjectReferences(TextWriter writer, SolutionFile solutionFile)
        {
            foreach (var solutionProjectFileReference in solutionFile.SolutionFileProjectReferences)
            {
                var projectLine = $@"Project(""{solutionProjectFileReference.ProjectTypeGUID.ToStringSolutionFileFormat()}"") = ""{solutionProjectFileReference.ProjectName}"", ""{solutionProjectFileReference.ProjectFileRelativePathValue}"", ""{solutionProjectFileReference.ProjectGUID.ToString("B").ToUpperInvariant()}""";
                writer.WriteLine(projectLine);

                writer.WriteLine("EndProject");
            }
        }

        private static void DeserializeGlobals(TextReader reader, ref string currentLine, SolutionFile solutionFile)
        {
            if (!SolutionFileTextSerializer.GlobalLineRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"Global\".\nFound: {currentLine}");
            }

            currentLine = reader.ReadLine().Trim();

            while (!SolutionFileTextSerializer.GlobalLineEndRegex.IsMatch(currentLine))
            {
                SolutionFileTextSerializer.DeserializeGlobal(reader, ref currentLine, solutionFile);

                currentLine = reader.ReadLine().Trim();
            }
        }

        private static void DeserializeGlobal(TextReader reader, ref string currentLine, SolutionFile solutionFile)
        {
            if (!SolutionFileTextSerializer.GlobalSectionRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"GlobalSection\".\nFound: {currentLine}");
            }

            var globalSectionMatches = Regex.Matches(currentLine, SolutionFileTextSerializer.GlobalSectionLineValuesRegexPattern);

            var sectionName = globalSectionMatches[0].Value.TrimStart('(').TrimEnd(')');
            var preOrPostSolutionStr = globalSectionMatches[1].Value;

            var preOrPostSolution = SolutionUtilities.ToPreOrPostSolution(preOrPostSolutionStr);

            ISolutionFileGlobalSection globalSection;
            switch(sectionName)
            {
                case SolutionConfigurationPlatformsGlobalSection.SolutionFileGlobalSectionName:
                    globalSection = SolutionFileTextSerializer.DeserializeSolutionConfigurationPlatformsGlobalSection(reader, ref currentLine, preOrPostSolution);
                    break;

                case ProjectConfigurationPlatformsGlobalSection.SolutionFileGlobalSectionName:
                    globalSection = SolutionFileTextSerializer.DeserializeProjectConfigurationPlatformsGlobalSection(reader, ref currentLine, preOrPostSolution);
                    break;

                case NestedProjectsSolutionFileGlobalSection.SolutionFileGlobalSectionName:
                    globalSection = SolutionFileTextSerializer.DeserializeNestedProjectsGlobalSection(reader, ref currentLine, preOrPostSolution);
                    break;

                default:
                    globalSection = SolutionFileTextSerializer.DeserializeGeneralGlobal(reader, ref currentLine, sectionName, preOrPostSolution);
                    break;
            }
            solutionFile.GlobalSections.Add(globalSection);
        }

        private static ProjectConfigurationPlatformsGlobalSection DeserializeProjectConfigurationPlatformsGlobalSection(TextReader reader, ref string currentLine, PreOrPostSolution preOrPostSolution)
        {
            var projectConfigurationPlatformsGlobalSection = new ProjectConfigurationPlatformsGlobalSection
            {
                Name = ProjectConfigurationPlatformsGlobalSection.SolutionFileGlobalSectionName,
                PreOrPostSolution = preOrPostSolution
            };

            currentLine = reader.ReadLine().Trim();

            while (!SolutionFileTextSerializer.GlobalSectionEndRegex.IsMatch(currentLine))
            {
                var assignmentTokens = currentLine.Split("=");

                var targetToken = assignmentTokens[0].Trim();
                var valueToken = assignmentTokens[1].Trim();

                var projectBuildConfigurationTokens = targetToken.Split(new string[] { Constants.SolutionProjectConfigurationTokenSeparator }, 3, StringSplitOptions.None);

                var projectGuidToken = projectBuildConfigurationTokens[0];
                var solutionBuildConfigurationToken = projectBuildConfigurationTokens[1];
                var indicatorToken = projectBuildConfigurationTokens[2];

                var projectGUID = Guid.Parse(projectGuidToken);
                var solutionBuildConfiguration = SolutionFileTextSerializer.DeserializeSolutionBuildConfiguration(solutionBuildConfigurationToken);
                var indicator = SolutionUtilities.ToProjectConfigurationIndicator(indicatorToken);
                var mappedSolutionBuildConfiguration = SolutionFileTextSerializer.DeserializeSolutionBuildConfiguration(valueToken);

                var projectBuildConfigurationMapping = new ProjectBuildConfigurationMapping
                {
                    ProjectGUID = projectGUID,
                    SolutionBuildConfiguration = solutionBuildConfiguration,
                    ProjectConfigurationIndicator = indicator,
                    MappedSolutionBuildConfiguration = mappedSolutionBuildConfiguration,
                };

                projectConfigurationPlatformsGlobalSection.ProjectBuildConfigurationMappings.Add(projectBuildConfigurationMapping);

                currentLine = reader.ReadLine().Trim();
            }

            return projectConfigurationPlatformsGlobalSection;
        }

        private static SolutionConfigurationPlatformsGlobalSection DeserializeSolutionConfigurationPlatformsGlobalSection(TextReader reader, ref string currentLine, PreOrPostSolution preOrPostSolution)
        {
            var solutionConfigurationPlatformsGlobalSection = new SolutionConfigurationPlatformsGlobalSection
            {
                Name = SolutionConfigurationPlatformsGlobalSection.SolutionFileGlobalSectionName,
                PreOrPostSolution = preOrPostSolution
            };

            currentLine = reader.ReadLine().Trim();

            while (!SolutionFileTextSerializer.GlobalSectionEndRegex.IsMatch(currentLine))
            {
                var assignmentTokens = currentLine.Split("=");

                var targetToken = assignmentTokens[0].Trim();
                var valueToken = assignmentTokens[1].Trim();

                var solutionBuildConfiguration = SolutionFileTextSerializer.DeserializeSolutionBuildConfiguration(targetToken);
                var mappedSolutionBuildConfiguration = SolutionFileTextSerializer.DeserializeSolutionBuildConfiguration(valueToken);

                var solutionBuildConfigurationMapping = new SolutionBuildConfigurationMapping
                {
                    SolutionBuildConfiguration = solutionBuildConfiguration,
                    MappedSolutionBuildConfiguration = mappedSolutionBuildConfiguration,
                };

                solutionConfigurationPlatformsGlobalSection.SolutionBuildConfigurationMappings.Add(solutionBuildConfigurationMapping);

                currentLine = reader.ReadLine().Trim();
            }

            return solutionConfigurationPlatformsGlobalSection;
        }

        private static SolutionBuildConfiguration DeserializeSolutionBuildConfiguration(string token)
        {
            var tokens = token.Split(Constants.SolutionBuildConfigurationTokenSeparator);

            var buildConfigurationToken = tokens[0].Trim();
            var platformTargetToken = tokens[1].Trim();

            var buildConfiguration = SolutionUtilities.ToBuildConfiguration(buildConfigurationToken);
            var platformTarget = SolutionUtilities.ToPlatformTarget(platformTargetToken);

            var solutionBuildConfiguration = new SolutionBuildConfiguration
            {
                BuildConfiguration = buildConfiguration,
                PlatformTarget = platformTarget,
            };
            return solutionBuildConfiguration;
        }

        private static NestedProjectsSolutionFileGlobalSection DeserializeNestedProjectsGlobalSection(TextReader reader, ref string currentLine, PreOrPostSolution preOrPostSolution)
        {
            var nestedProjectGlobalSection = new NestedProjectsSolutionFileGlobalSection
            {
                Name = NestedProjectsSolutionFileGlobalSection.SolutionFileGlobalSectionName,
                PreOrPostSolution = preOrPostSolution
            };

            currentLine = reader.ReadLine().Trim();

            while (!SolutionFileTextSerializer.GlobalSectionEndRegex.IsMatch(currentLine))
            {
                var projectNesting = ProjectNesting.Deserialize(currentLine);
                nestedProjectGlobalSection.ProjectNestings.Add(projectNesting);

                currentLine = reader.ReadLine().Trim();
            }

            return nestedProjectGlobalSection;
        }

        private static GeneralSolutionFileGlobalSection DeserializeGeneralGlobal(TextReader reader, ref string currentLine, string sectionName, PreOrPostSolution preOrPostSolution)
        {
            var globalSection = new GeneralSolutionFileGlobalSection
            {
                Name = sectionName,
                PreOrPostSolution = preOrPostSolution,
            };

            currentLine = reader.ReadLine().Trim();

            while (!SolutionFileTextSerializer.GlobalSectionEndRegex.IsMatch(currentLine))
            {
                globalSection.Lines.Add(currentLine);

                currentLine = reader.ReadLine().Trim();
            }

            return globalSection;
        }

        private static void DeserializeProjects(TextReader reader, ref string currentLine, SolutionFile solutionFile)
        {
            if (!SolutionFileTextSerializer.ProjectLineRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"Project...\".\nFound: {currentLine}");
            }

            while (!SolutionFileTextSerializer.GlobalLineRegex.IsMatch(currentLine))
            {
                SolutionFileTextSerializer.DeserializeProject(reader, ref currentLine, solutionFile);

                currentLine = reader.ReadLine();
            }
        }

        private static void DeserializeProject(TextReader reader, ref string currentLine, SolutionFile solutionFile)
        {
            if (!SolutionFileTextSerializer.ProjectLineRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"Project...\".\nFound: {currentLine}");
            }

            var matches = Regex.Matches(currentLine, SolutionFileTextSerializer.ProjectLineValuesRegexPattern);

            var projectTypeGUIDStr = matches[0].Value.Trim('"');
            var projectName = matches[1].Value.Trim('"');
            var projectFileRelativePathValue = matches[2].Value.Trim('"');
            var projectGUIDStr = matches[3].Value.Trim('"');

            var projectTypeGUID = Guid.Parse(projectTypeGUIDStr);
            var projectGUID = Guid.Parse(projectGUIDStr);

            var solutionProjectFileReference = new SolutionFileProjectReference
            {
                ProjectTypeGUID = projectTypeGUID,
                ProjectName = projectName,
                ProjectFileRelativePathValue = projectFileRelativePathValue,
                ProjectGUID = projectGUID
            };

            solutionFile.SolutionFileProjectReferences.Add(solutionProjectFileReference);

            currentLine = reader.ReadLine();
            if (!SolutionFileTextSerializer.ProjectLineEndRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"EndProject\".\nFound: {currentLine}");
            }
        }

        #endregion


        public SolutionFile Deserialize(TextReader reader)
        {
            var solutionFile = new SolutionFile();

            var blankBeginLine = reader.ReadLine();
            var formatVersionLine = reader.ReadLine();
            var monikerLine = reader.ReadLine();
            var vsVersionLine = reader.ReadLine();
            var vsMinimumVersionLine = reader.ReadLine();

            var formatVersionTokens = formatVersionLine.Split(' ');
            var formatVersionToken = formatVersionTokens.Last();
            solutionFile.FormatVersion = Version.Parse(formatVersionToken);

            solutionFile.VisualStudioMoniker = monikerLine;

            var vsVersionTokens = vsVersionLine.Split(' ');
            var vsVersionToken = vsVersionTokens.Last();
            solutionFile.VisualStudioVersion = Version.Parse(vsVersionToken);

            var vsMinimumVersionTokens = vsMinimumVersionLine.Split(' ');
            var vsMinimumVersionToken = vsMinimumVersionTokens.Last();
            solutionFile.MinimumVisualStudioVersion = Version.Parse(vsMinimumVersionToken);

            var currentLine = reader.ReadLine();

            if(SolutionFileTextSerializer.ProjectLineRegex.IsMatch(currentLine))
            {
                SolutionFileTextSerializer.DeserializeProjects(reader, ref currentLine, solutionFile);
            }

            if(!SolutionFileTextSerializer.GlobalLineRegex.IsMatch(currentLine))
            {
                throw new Exception($"Unknown line.\nExpected: \"Global\".\nFound: {currentLine}");
            }

            SolutionFileTextSerializer.DeserializeGlobals(reader, ref currentLine, solutionFile);

            var blankEndLine = reader.ReadLine();

            if(reader.ReadToEnd() != String.Empty)
            {
                throw new Exception("Reader was not at end.");
            }

            return solutionFile;
        }

        

        public void Serialize(TextWriter writer, SolutionFile solutionFile)
        {
            writer.WriteLine(); // Blank first line.

            var formatVersionLine = $"Microsoft Visual Studio Solution File, Format Version {solutionFile.FormatVersion.Major}.{solutionFile.FormatVersion.Minor:00}";
            writer.WriteLine(formatVersionLine);
            writer.WriteLine(solutionFile.VisualStudioMoniker);
            var vsVersionLine = $"VisualStudioVersion = {solutionFile.VisualStudioVersion}";
            writer.WriteLine(vsVersionLine);
            var vsMinimumVersionLine = $"MinimumVisualStudioVersion = {solutionFile.MinimumVisualStudioVersion}";
            writer.WriteLine(vsMinimumVersionLine);

            SolutionFileTextSerializer.SerializeProjectReferences(writer, solutionFile);

            SolutionFileTextSerializer.SerializeGlobals(writer, solutionFile);

            // Blank last line for free due to prior WriteLine().
        }
    }
}
