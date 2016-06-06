// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using live.asp.net.Models;

namespace live.asp.net.Services
{
    public class ShowList
    {
        public IList<Show> Shows { get; set; }

        /// <summary>
        /// URL zu Youtube, wo es noch mehr shows gibt.
        /// </summary>
        public string MoreShowsUrl { get; set; }
    }
}
