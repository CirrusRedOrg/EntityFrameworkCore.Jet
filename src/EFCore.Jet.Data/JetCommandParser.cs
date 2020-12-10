using System;
using System.Collections.Generic;
using System.Linq;

namespace EntityFrameworkCore.Jet.Data
{
    public class JetCommandParser
    {
        public string SqlFragment { get; }
        public char[] States { get; }

        public JetCommandParser(string sqlFragment)
        {
            SqlFragment = sqlFragment ?? throw new ArgumentNullException(nameof(sqlFragment));
            States = new char[sqlFragment.Length];

            Parse();
        }

        public IReadOnlyList<int> GetStateIndices(char state, int start = 0, int length = -1)
        {
            if (start < 0 ||
                start >= States.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (length < 0 ||
                length > 0 && start + length > States.Length)
            {
                length = States.Length - start;
            }

            var stateIndices = new List<int>();
            char? lastState = null;

            for (var i = start; i < length; i++)
            {
                var currentState = States[i];

                if (currentState == state &&
                    currentState != lastState)
                {
                    stateIndices.Add(i);
                }

                lastState = currentState;
            }

            return stateIndices.AsReadOnly();
        }

        public IReadOnlyList<int> GetStateIndices(char[] states, int start = 0, int length = -1)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            if (states.Length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(states));
            }

            if (start < 0 ||
                start >= States.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (length < 0 ||
                length > 0 && start + length > States.Length)
            {
                length = States.Length - start;
            }

            var stateIndices = new List<int>();
            char? lastState = null;

            for (var i = start; i < length; i++)
            {
                var currentState = States[i];

                if (currentState != lastState &&
                    states.Contains(currentState))
                {
                    stateIndices.Add(i);
                }

                lastState = currentState;
            }

            return stateIndices.AsReadOnly();
        }

        protected virtual void Parse()
        {
            // We use '\0' as the default state and char.
            var state = '\0';
            var lastChar = '\0';

            // State machine to parse ODBC and OleDB SQL.
            for (var i = 0; i < SqlFragment.Length; i++)
            {
                var c = SqlFragment[i];

                if (state == '\'')
                {
                    // We are currently inside a string, or closed the string in the last iteration but didn't
                    // know that at the time, because it still could have been the beginning of an escape sequence.

                    if (c == '\'')
                    {
                        // We either end the string, begin an escape sequence or end an escape sequence.
                        if (lastChar == '\'')
                        {
                            // This is the end of an escape sequence.
                            // We continue being in a string.
                            lastChar = '\0';
                        }
                        else
                        {
                            // This is either the beginning of an escape sequence, or the end of the string.
                            // We will know the in the next iteration.
                            lastChar = '\'';
                        }
                    }
                    else if (lastChar == '\'')
                    {
                        // The last iteration was the end of a string.
                        // Reset the current state and continue processing the current char.
                        state = '\0';
                        lastChar = '\0';
                        States[i - 1] = state;
                    }
                }

                if (state == '"')
                {
                    // We are currently inside a string, or closed the string in the last iteration but didn't
                    // know that at the time, because it still could have been the beginning of an escape sequence.

                    if (c == '"')
                    {
                        // We either end the string, begin an escape sequence or end an escape sequence.
                        if (lastChar == '"')
                        {
                            // This is the end of an escape sequence.
                            // We continue being in a string.
                            lastChar = '\0';
                        }
                        else
                        {
                            // This is either the beginning of an escape sequence, or the end of the string.
                            // We will know the in the next iteration.
                            lastChar = '"';
                        }
                    }
                    else if (lastChar == '"')
                    {
                        // The last iteration was the end of a string.
                        // Reset the current state and continue processing the current char.
                        state = '\0';
                        lastChar = '\0';
                        States[i - 1] = state;
                    }
                }

                if (state == '\0')
                {
                    if (c == '"')
                    {
                        state = '"';
                    }
                    else if (c == '\'')
                    {
                        state = '\'';
                    }
                    else if (c == '`')
                    {
                        state = '`';
                    }
                    else if (c == '[')
                    {
                        state = '[';
                    }
                    else if (c == '?')
                    {
                        States[i] = '?';
                    }
                    else if (c == '@')
                    {
                        // This is either the beginning of a named parameter, or a global variable like @@identity.
                        state = '@';
                    }
                    else if (c == ';')
                    {
                        States[i] = ';';
                    }
                }
                else if (state == '`' &&
                         c == '`')
                {
                    state = '\0';
                }
                else if (state == '[' &&
                         c == ']')
                {
                    state = '\0';
                }
                else if (state == '@')
                {
                    if (c == '@')
                    {
                        // This has not been a named parameter, but a global variable.
                        // We use '$' to signal a global variable like @@identity.
                        States[i - 1] = '$';
                    }

                    state = '\0';
                }

                if (state != '\0')
                {
                    States[i] = state;
                }
            }

            //
            // Handle still pending states:
            //

            if (state == '\'' && lastChar == '\'' ||
                state == '"' && lastChar == '"')
            {
                // The last iteration was the end of a string.
                state = '\0';
                lastChar = '\0';
                States[States.Length - 1] = state;
            }
        }
    }
}