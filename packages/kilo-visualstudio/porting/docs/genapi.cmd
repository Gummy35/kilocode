SET JAVA_HOME="C:\Program Files\JetBrains\IntelliJ IDEA Community Edition 2025.1.4.1\jbr"
SET PATH=%PATH%;%JAVA_HOME%\bin

npx @openapitools/openapi-generator-cli generate -i openapi-spec.json -g csharp --skip-validate-spec --library httpclient --additional-properties=validatable=false