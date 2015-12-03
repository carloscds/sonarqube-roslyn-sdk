//-----------------------------------------------------------------------
// <copyright file="PluginBuilder.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------
using Roslyn.SonarQube.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace Roslyn.SonarQube.PluginGenerator
{
    public class PluginBuilder
    {
        public const string SONARQUBE_API_VERSION = "4.5.2";

        private readonly IJdkWrapper jdkWrapper;
        private readonly ILogger logger;

        private readonly ISet<string> sourceFiles;
        private readonly ISet<string> referencedJars;

        private readonly IDictionary<string, string> properties;
        private readonly IDictionary<string, string> fileToRelativePathMap;

        private string outputJarFilePath;

        /// <summary>
        /// List of jar files that are available at runtime and so do not
        /// need to be embedded in the jar file
        /// </summary>
        private static readonly string[] availableJarFiles = new string[]
        {
            "sonar-plugin-api-4.5.2.jar",
            "slf4j-api-1.7.5.jar" // available in the sonar-plugin-api
        };

        public PluginBuilder(IJdkWrapper jdkWrapper, ILogger logger)
        {
            if (jdkWrapper == null)
            {
                throw new ArgumentNullException("jdkWrapper");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            this.logger = logger;
            this.jdkWrapper = jdkWrapper;

            this.sourceFiles = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            this.referencedJars = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            this.properties = new Dictionary<string, string>();
            this.fileToRelativePathMap = new Dictionary<string, string>();
        }

        public PluginBuilder(ILogger logger) : this(new JdkWrapper(), logger)
        {
        }

        public PluginBuilder SetJarFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentNullException("name");
            }

            this.outputJarFilePath = filePath;

            return this;
        }

        /// <summary>
        /// Sets a plugin property (i.e. a property that will appear in the manifest file)
        /// </summary>
        public PluginBuilder SetProperty(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException("name");
            }

            this.properties[name] = value;
            return this;
        }

        /// <summary>
        /// Adds a source file to be compiled into the plugin
        /// </summary>
        public PluginBuilder AddSourceFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentNullException("sourcePath");
            }
            this.sourceFiles.Add(sourcePath);
            return this;
        }

        /// <summary>
        /// Adds a reference to a jar file that is required to compile the source
        /// </summary>
        public PluginBuilder AddReferencedJar(string fullJarFilePath)
        {
            if (string.IsNullOrWhiteSpace(fullJarFilePath))
            {
                throw new ArgumentNullException("fullJarFilePath");
            }
            this.referencedJars.Add(fullJarFilePath);
            return this;
        }

        /// <summary>
        /// Adds a file to the jar. The location of the file in the jar
        /// is specified by the <paramref name="relativeJarPath"/>.
        /// </summary>
        public PluginBuilder AddResourceFile(string fullFilePath, string relativeJarPath)
        {
            if (string.IsNullOrWhiteSpace(fullFilePath))
            {
                throw new ArgumentNullException("fullFilePath");
            }

            this.fileToRelativePathMap[fullFilePath] = relativeJarPath;

            return this;
        }

        /// <summary>
        /// Compiles the source files that have been supplied and builds the jar file
        /// </summary>
        public void Build()
        {
            if (!this.jdkWrapper.IsJdkInstalled())
            {
                throw new InvalidOperationException(UIResources.JarB_JDK_NotInstalled);
            }

            // TODO: validate inputs

            // Temp working folder
            string tempWorkingDir = Path.Combine(Path.GetTempPath(), "plugins",  Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempWorkingDir);

            // Unpack and reference the required jar files
            SourceGenerator.UnpackReferencedJarFiles(typeof(RulesPluginGenerator).Assembly, "Roslyn.SonarQube.PluginGenerator.Resources", tempWorkingDir);
            foreach (string jarFile in Directory.GetFiles(tempWorkingDir, "*.jar"))
            {
                this.AddReferencedJar(jarFile);
            }

            // Compile sources
            CompileJavaFiles(tempWorkingDir);

            // Build jar
            BuildJar(tempWorkingDir);
        }

        #region Private methods

        private void CompileJavaFiles(string workingDirectory)
        {
            JavaCompilationBuilder compiler = new JavaCompilationBuilder(this.jdkWrapper);

            foreach (string jarFile in this.referencedJars)
            {
                compiler.AddClassPath(jarFile);
            }

            foreach (string sourceFile in this.sourceFiles)
            {
                compiler.AddSources(sourceFile);
            }

            bool success = compiler.Compile(workingDirectory, workingDirectory, this.logger);

            if (success)
            {
                logger.LogInfo(UIResources.JComp_SourceCompilationSucceeded);
            }
            else
            {
                logger.LogError(UIResources.JComp_SourceCompilationFailed);
                throw new CompilerException(UIResources.JComp_CompliationFailed);
            }
        }

        private bool BuildJar(string classesDirectory)
        {
            JarBuilder jarBuilder = new JarBuilder(logger, this.jdkWrapper);

            // Set the manifest properties
            jarBuilder.SetManifestPropety("Sonar-Version", SONARQUBE_API_VERSION);

            foreach (KeyValuePair<string, string> nameValuePair in this.properties)
            {
                jarBuilder.SetManifestPropety(nameValuePair.Key, nameValuePair.Value);
            }

            // Add the generated classes
            int lenClassPath = classesDirectory.Length + 1;
            foreach (string classFile in Directory.GetFiles(classesDirectory, "*.class", SearchOption.AllDirectories))
            {
                jarBuilder.AddFile(classFile, classFile.Substring(lenClassPath));
            }

            // Add any other content files
            foreach(KeyValuePair<string, string> pathToFilePair in this.fileToRelativePathMap)
            {
                jarBuilder.AddFile(pathToFilePair.Key, pathToFilePair.Value);
            }

            // Embed all referenced jars into the jar
            // NB not all jars need to be added
            StringBuilder sb = new StringBuilder();
            foreach (string refJar in this.referencedJars)
            {
                if (IsJarAvailable(refJar))
                {
                    continue;
                }

                string jarName = "META-INF/lib/" + Path.GetFileName(refJar);
                jarBuilder.AddFile(refJar, jarName);

                sb.Append(jarName);
                sb.Append(" ");
            }
            jarBuilder.SetManifestPropety("Plugin-Dependencies", sb.ToString());

            

            return jarBuilder.Build(this.outputJarFilePath);
        }

        private static bool IsJarAvailable(string resourceName)
        {
            return availableJarFiles.Any(j => resourceName.EndsWith(j));
        }

        #endregion
    }
}
