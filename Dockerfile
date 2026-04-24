# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

EXPOSE 8080

# Copy only the project files needed for restore so Docker can cache dependencies.
COPY MyBlog/MyBlog.csproj MyBlog/
COPY MyBlog.AislePilot/MyBlog.AislePilot.csproj MyBlog.AislePilot/
RUN dotnet restore MyBlog/MyBlog.csproj

# Copy only the app projects needed for publish.
COPY MyBlog/ MyBlog/
COPY MyBlog.AislePilot/ MyBlog.AislePilot/

# Publish the app
RUN dotnet publish MyBlog/MyBlog.csproj -c Release -o /app/out --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "MyBlog.dll"]
