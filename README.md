# Система анализа текстовых файлов

Микросервисная система для загрузки, хранения и анализа текстовых файлов.

## Архитектура

Система состоит из трех микросервисов:

1. **FileStoringService** (https://localhost:7001)
   - Отвечает за хранение файлов
   - Предоставляет gRPC API для загрузки и получения файлов

2. **FileAnalysisService** (https://localhost:7002)
   - Отвечает за анализ текстовых файлов
   - Предоставляет gRPC API для:
     - Подсчета слов и символов
     - Генерации облака слов
     - Анализа текста

3. **ApiGateway** (https://localhost:7000)
   - Единая точка входа для клиентов
   - Предоставляет REST API для взаимодействия с другими сервисами
   - Включает Swagger UI для тестирования API

## API Endpoints

### ApiGateway

#### Загрузка файла
```
POST /api/files/upload
Content-Type: multipart/form-data
```

#### Получение файла
```
GET /api/files/{fileId}
```

#### Получение анализа файла
```
GET /api/files/getanalysis/{fileId}
```

#### Получение облака слов
```
GET /api/files/{fileId}/wordcloud
```

## Запуск системы

1. Запустите FileStoringService:
```powershell
cd FileStoringService
dotnet run
```

2. Запустите FileAnalysisService:
```powershell
cd FileAnalysisService
dotnet run
```

3. Запустите ApiGateway:
```powershell
cd ApiGateway
dotnet run
```

## Тестирование API

После запуска всех сервисов, откройте Swagger UI по адресу:
https://localhost:7000/swagger

Здесь вы можете:
- Загружать файлы
- Получать результаты анализа
- Просматривать облака слов
- Тестировать все доступные эндпоинты

## Технологии

- .NET 7
- gRPC
- ASP.NET Core
- Swagger/OpenAPI
- Entity Framework Core
- SQLite 