# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

EXPOSE 8080

# Copy the project file and restore dependencies
# Reference MyBlog.csproj relatively from the Dockerfile's location (in MyBlog/MyBlog)
COPY MyBlog/MyBlog.csproj ./MyBlog/

WORKDIR /app/MyBlog
RUN dotnet restore MyBlog.csproj

# Copy the rest of the code (everything in the same directory as the Dockerfile)
COPY . ./ 


# Publish the app
RUN dotnet publish MyBlog/MyBlog.csproj -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "MyBlog.dll"]
