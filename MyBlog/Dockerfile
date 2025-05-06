# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore
COPY MyBlog/*.csproj ./MyBlog/
RUN dotnet restore ./MyBlog/MyBlog.csproj

# Copy the rest of the code
COPY . .

# Publish the app
RUN dotnet publish MyBlog/MyBlog.csproj -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "MyBlog.dll"]
