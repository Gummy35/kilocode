using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for connection service question handling - mirrors connection-service-question.test.ts from VS Code
    /// </summary>
    public class QuestionHandlingTests
    {
        private class Question
        {
            public string Id { get; set; }
            public string SessionID { get; set; }
            public string[] Questions { get; set; }
            public bool Blocking { get; set; }
        }

        private class QuestionStore
        {
            public Dictionary<string, Question> PendingQuestions { get; } = new Dictionary<string, Question>();
            public List<string> AnsweredQuestions { get; } = new List<string>();

            public void AddQuestion(Question question)
            {
                PendingQuestions[question.Id] = question;
            }

            public void AnswerQuestion(string questionId, string[] answers)
            {
                if (PendingQuestions.ContainsKey(questionId))
                {
                    PendingQuestions.Remove(questionId);
                    AnsweredQuestions.Add(questionId);
                }
            }

            public Question GetPendingQuestion(string questionId)
            {
                PendingQuestions.TryGetValue(questionId, out var question);
                return question;
            }

            public bool HasPendingQuestion(string questionId)
            {
                return PendingQuestions.ContainsKey(questionId);
            }
        }

        [Fact]
        public void Stores_and_retrieves_pending_question()
        {
            // Arrange
            var store = new QuestionStore();
            var question = new Question 
            { 
                Id = "q1", 
                SessionID = "s1", 
                Questions = new[] { "What should I do?" },
                Blocking = true
            };

            // Act
            store.AddQuestion(question);

            // Assert
            store.HasPendingQuestion("q1").Should().BeTrue();

            var retrieved = store.GetPendingQuestion("q1");
            retrieved.Should().NotBeNull();
            retrieved.SessionID.Should().Be("s1");
            retrieved.Blocking.Should().BeTrue();
        }

        [Fact]
        public void Answers_question_and_removes_from_pending()
        {
            // Arrange
            var store = new QuestionStore();
            store.AddQuestion(new Question { Id = "q1", SessionID = "s1", Questions = new[] { "Q1" } });

            // Act
            store.AnswerQuestion("q1", new[] { "A1" });

            // Assert
            store.HasPendingQuestion("q1").Should().BeFalse();
            store.AnsweredQuestions.Should().Contain("q1");
        }

        [Fact]
        public void Does_not_answer_non_existent_question()
        {
            // Arrange
            var store = new QuestionStore();

            // Act
            store.AnswerQuestion("non-existent", new[] { "A1" });

            // Assert
            store.AnsweredQuestions.Count.Should().Be(0);
        }
    }
}
