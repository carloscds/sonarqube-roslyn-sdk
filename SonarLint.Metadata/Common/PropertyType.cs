﻿//-----------------------------------------------------------------------
// <copyright file="RuleGenerator.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace SonarLint.Common
{
    public enum PropertyType
    {
        String,
        Text,
        Password,
        Boolean,
        Integer,
        Float,
        SingleSelectList,
        Metric,
        License,
        RegularExpression,
        PropertySet,
        UserLogin
    }
}
