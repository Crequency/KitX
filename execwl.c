#include <unistd.h>
#include <stdlib.h>

void exec_with_lc(const char * path, const char * file){
    putenv("LC_ALL=C");
    char * args[2];
    args[0] = file;
    args[2] = NULL;
    execv(path, args);
}
