﻿using System;

namespace Raven.NewClient.Client
{

    public class BulkInsertProtocolViolationExeption : Exception
    {
        public BulkInsertProtocolViolationExeption(string message) : base(message)
        {
        }
    }

    public class BulkInsertAbortedExeption : Exception
    {
        public BulkInsertAbortedExeption(string message) : base(message)
        {
        }
    }
}