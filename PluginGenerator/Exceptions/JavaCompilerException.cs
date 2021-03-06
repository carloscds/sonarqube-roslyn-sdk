//-----------------------------------------------------------------------
// <copyright file="JavaCompilerException.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------
using System;

namespace SonarQube.Plugins
{
    [Serializable]
    public class JavaCompilerException : Exception
    {
        public JavaCompilerException() { }
        public JavaCompilerException(string message) : base(message) { }
        public JavaCompilerException(string message, Exception inner) : base(message, inner) { }
        protected JavaCompilerException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context)
        { }
    }
}
