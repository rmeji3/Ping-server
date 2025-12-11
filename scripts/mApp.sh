#!/bin/bash
dotnet ef migrations add $1 --context AppDbContext
dotnet ef database update --context AppDbContext