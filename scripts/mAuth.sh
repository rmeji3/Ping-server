#!/bin/bash
dotnet ef migrations add $1 --context AuthDbContext
dotnet ef database update --context AuthDbContext.