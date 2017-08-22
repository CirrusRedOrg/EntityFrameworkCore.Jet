using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model30
{
    public class Answer
    {
        [Key]
        public int AnswerId { get; set; }
        public string AnswerText { get; set; }
        //public int Question_QuestionId { get; set; }
        public virtual Question Question { get; set; }
    }


    public class Question
    {
        [Key]
        public int QuestionId { get; set; }
        public string QuestionText { get; set; }
        public virtual Answer CorrectAnswer { get; set; }
        public virtual List<Answer> Answers { get; set; }
    }

}
